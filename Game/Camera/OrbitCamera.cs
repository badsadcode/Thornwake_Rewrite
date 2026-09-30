using Godot;
using Thornwake.Core;

namespace Thornwake.Camera;

/// <summary>Cursor ownership and orbit presentation. Menus and focus loss always release capture.</summary>
public partial class OrbitCamera : Node
{
    public readonly OrbitState Orbit = new();
    public readonly PointerGestures Pointer = new();
    public bool AlwaysRotate { get; private set; }
    public float Sensitivity { get; private set; } = 1;
    public bool InvertY { get; private set; }
    public bool Captured => Input.MouseMode == Input.MouseModeEnum.Captured;
    public bool AlwaysActive => AlwaysRotate && !_placement;
    public bool CanControl => _enabled && _focused;
    public bool IsFocused => _focused;
    private bool _enabled, _placement, _focused = true, _ignoreNextMotion;
    private Vector2 _savedCursor;
    private Vector3 _focus;
    private bool _hasFocus;

    public void Configure(bool always, float sensitivity, bool inverted)
    {
        AlwaysRotate = always;
        Sensitivity = Mathf.Clamp(float.IsFinite(sensitivity) ? sensitivity : 1, .3f, 2.5f);
        InvertY = inverted;
        Pointer.Cancel();
        RefreshCapture();
    }
    public void SetControl(bool enabled, bool placement)
    {
        if (enabled != _enabled || placement != _placement) Pointer.Cancel();
        _enabled = enabled;
        _placement = placement;
        RefreshCapture();
    }
    public void SetFocused(bool focused) { _focused = focused; Pointer.Cancel(); RefreshCapture(); }
    public void Release() { _enabled = false; Pointer.Cancel(); RefreshCapture(); }
    public bool HandleButton(InputEventMouseButton button)
    {
        if (!CanControl) return false;
        if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed) { Orbit.Zoom(-1); return true; }
        if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed) { Orbit.Zoom(1); return true; }
        PointerButton? mapped = button.ButtonIndex switch { MouseButton.Left => PointerButton.Left, MouseButton.Right => PointerButton.Right, MouseButton.Middle => PointerButton.Middle, _ => null };
        if (!mapped.HasValue) return false;
        Pointer.Button(mapped.Value, button.Pressed, !AlwaysActive);
        RefreshCapture();
        return true;
    }
    public void Motion(InputEventMouseMotion motion)
    {
        if (!CanControl || !Captured) return;
        if (_ignoreNextMotion) { _ignoreNextMotion = false; return; }
        Orbit.Look(motion.ScreenRelative.X, motion.ScreenRelative.Y, Sensitivity, InvertY);
    }
    private void RefreshCapture()
    {
        bool capture = CanControl && (AlwaysActive || Pointer.Orbiting) && !Input.IsPhysicalKeyPressed(Key.Tab);
        if (capture == Captured) return;
        if (capture)
        {
            _savedCursor = GetViewport().GetMousePosition();
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _ignoreNextMotion = true;
        }
        else
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (_focused) Input.WarpMouse(_savedCursor);
        }
    }
    public void StepInput(float dt, bool rotateBuilding)
    {
        RefreshCapture();
        if (CanControl)
        {
            float turn = (Input.IsPhysicalKeyPressed(Key.R) && !rotateBuilding ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0);
            Orbit.Turn(turn, dt);
            Pointer.Tick(dt);
        }
        Orbit.Step(dt);
    }
    public void Follow(Camera3D camera, Vector3 target, float dt)
    {
        _focus = _hasFocus ? _focus.Lerp(target, 1 - Mathf.Exp(-dt * 12)) : target;
        _hasFocus = true;
        float horizontal = Mathf.Cos(Orbit.Pitch) * Orbit.Distance;
        var offset = new Vector3(0, Mathf.Sin(Orbit.Pitch) * Orbit.Distance, horizontal).Rotated(Vector3.Up, Orbit.Yaw);
        camera.Position = _focus + offset;
        camera.LookAt(_focus + Vector3.Up * .6f);
    }
    public void SnapFocus(Vector3 target) { _focus = target; _hasFocus = true; }
    public override void _ExitTree() { Input.MouseMode = Input.MouseModeEnum.Visible; }
}
