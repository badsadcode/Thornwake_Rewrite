using System;

namespace Thornwake.Core;

/// <summary>Frame-rate independent orbit targets, shared by mouse and keyboard input.</summary>
public sealed class OrbitState
{
    public const float DefaultPitch = .675f;
    public const float DefaultDistance = 29.45f;
    public float Yaw { get; private set; } = -.35f;
    public float Pitch { get; private set; } = DefaultPitch;
    public float Distance { get; private set; } = DefaultDistance;
    public float TargetYaw { get; private set; } = -.35f;
    public float TargetPitch { get; private set; } = DefaultPitch;
    public float TargetDistance { get; private set; } = DefaultDistance;

    public void Look(float x, float y, float sensitivity, bool invertY)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) return;
        float speed = Math.Clamp(float.IsFinite(sensitivity) ? sensitivity : 1, .3f, 2.5f) * .0035f;
        TargetYaw -= x * speed;
        TargetPitch = Math.Clamp(TargetPitch + y * speed * (invertY ? -1 : 1), .32f, 1.2f);
    }
    public void Turn(float direction, float seconds) => TargetYaw += Math.Clamp(direction, -1, 1) * 1.85f * Math.Clamp(seconds, 0, .1f);
    public void Zoom(float steps) { if (float.IsFinite(steps)) TargetDistance = Math.Clamp(TargetDistance + steps * 2.5f, 14, 48); }
    public void Reset() { TargetYaw = -.35f; TargetPitch = DefaultPitch; TargetDistance = DefaultDistance; }
    public void Step(float seconds)
    {
        float blend = 1 - MathF.Exp(-14 * Math.Clamp(seconds, 0, .1f));
        Yaw += (TargetYaw - Yaw) * blend;
        Pitch += (TargetPitch - Pitch) * blend;
        Distance += (TargetDistance - Distance) * blend;
    }
}

public enum PointerButton { Left, Right, Middle }

/// <summary>Arbitrates the orbit chord before dispatching gameplay clicks.</summary>
public sealed class PointerGestures
{
    public bool Left { get; private set; }
    public bool Right { get; private set; }
    public bool Middle { get; private set; }
    public bool ChordConsumed { get; private set; }
    public bool Orbiting => Middle || (Left && Right && ChordConsumed);
    public bool Blocking => Right && !ChordConsumed;
    private bool _pendingPrimary, _primary, _secondary;
    private float _primaryWait;

    public void Button(PointerButton button, bool pressed, bool allowChord)
    {
        bool wasRight=Right;
        switch (button)
        {
            case PointerButton.Left: Left = pressed; break;
            case PointerButton.Right: Right = pressed; break;
            case PointerButton.Middle: Middle = pressed; break;
        }
        if (allowChord && !Middle && Left && Right)
        {
            ChordConsumed = true;
            _pendingPrimary = false;
            _primary = _secondary = false;
        }
        if (button == PointerButton.Left && pressed && !ChordConsumed)
        {
            // Briefly defer a drag-mode primary press, so the second button can claim it.
            if (allowChord && !Middle) { _pendingPrimary = true; _primaryWait = .08f; }
            else _primary = true;
        }
        if (button == PointerButton.Left && !pressed && _pendingPrimary)
        {
            _primary = !ChordConsumed;
            _pendingPrimary = false;
        }
        if (button == PointerButton.Right && wasRight && !pressed && !ChordConsumed) _secondary = true;
        if (!Left && !Right) ChordConsumed = false;
    }
    public void Tick(float seconds)
    {
        if (!_pendingPrimary) return;
        _primaryWait -= seconds;
        if (_primaryWait <= 0) { _pendingPrimary = false; _primary = !ChordConsumed; }
    }
    public bool TakePrimary() { bool value = _primary; _primary = false; return value; }
    public bool TakeSecondary() { bool value = _secondary; _secondary = false; return value; }
    public void Cancel() { Left = Right = Middle = ChordConsumed = _pendingPrimary = _primary = _secondary = false; }
}
