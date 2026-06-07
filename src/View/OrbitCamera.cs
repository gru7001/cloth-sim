using Godot;

namespace DelaunyFabric.View;

public partial class OrbitCamera : Camera3D
{
    [Export] public Vector3 Target { get; set; } = new(0f, 1.2f, 0f);
    [Export] public float Distance { get; set; } = 5f;
    [Export] public float MinDistance { get; set; } = 0.0f;
    [Export] public float MaxDistance { get; set; } = 12f;
    [Export] public float OrbitSensitivity { get; set; } = 0.004f;
    [Export] public float ZoomSensitivity { get; set; } = 0.12f;
    [Export] public float MinPitch { get; set; } = -1.45f;
    [Export] public float MaxPitch { get; set; } = 1.45f;

    float _yaw;
    float _pitch = 0.55f;
    bool _dragging;

    public override void _Ready()
    {
        InitFromCurrentTransform();
        ApplyTransform();
    }

    void InitFromCurrentTransform()
    {
        Vector3 offset = Position - Target;
        if (offset.LengthSquared() < 1e-6f) return;
        Distance = offset.Length();
        _pitch = Mathf.Asin(Mathf.Clamp(offset.Y / Distance, -1f, 1f));
        _yaw = Mathf.Atan2(offset.X, offset.Z);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } btn)
        {
            _dragging = btn.Pressed;
            Input.MouseMode = btn.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        }

        if (@event is InputEventMouseMotion motion && _dragging)
        {
            _yaw -= motion.Relative.X * OrbitSensitivity;
            _pitch += motion.Relative.Y * OrbitSensitivity;
            _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
            ApplyTransform();
        }

        if (@event is InputEventMouseButton { Pressed: true } wheel)
        {
            if (wheel.ButtonIndex == MouseButton.WheelUp)
                Distance = Mathf.Max(MinDistance, Distance * (1f - ZoomSensitivity));
            if (wheel.ButtonIndex == MouseButton.WheelDown)
                Distance = Mathf.Min(MaxDistance, Distance * (1f + ZoomSensitivity));
            ApplyTransform();
        }
    }

    void ApplyTransform()
    {
        float cp = Mathf.Cos(_pitch);
        float dist = Distance;
        Vector3 offset = new(cp * Mathf.Sin(_yaw) * dist, Mathf.Sin(_pitch) * dist, cp * Mathf.Cos(_yaw) * dist);
        Position = Target + offset;
        LookAt(Target);
    }
}
