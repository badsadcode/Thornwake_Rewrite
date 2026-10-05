using System;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// World map for exploring and testing (M): terrain or biome view (B), mouse wheel zooms at the cursor, left-drag pans,
/// right-click teleports there (onto the ground). The marker shows the player and the way they face.
/// </summary>
public partial class WorldMap : Control
{
    private const int Pixels = 768;
    private readonly TextureRect _image = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
    private readonly Label _marker = new() { Text = "▲", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label _info = new();
    private WorldPlan? _plan;
    private double _cx, _cz, _span;
    private bool _biomes, _dragging;
    private double _playerX, _playerZ; private float _playerYaw;

    /// <summary>Called with world (x, z) on right-click.</summary>
    public Action<double, double>? Teleport;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = new Color(0, 0, 0, .78f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });
        _image.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_image);
        _marker.AddThemeColorOverride("font_color", new Color(1, .25f, .2f));
        _marker.AddThemeColorOverride("font_outline_color", Colors.Black);
        _marker.AddThemeConstantOverride("outline_size", 6);
        _marker.AddThemeFontSizeOverride("font_size", 22);
        _marker.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_marker);
        _info.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _info.Position = new Vector2(16, 0);
        _info.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_info);
        Visible = false;
        Resized += Layout;
    }

    public void Open(WorldPlan plan, double playerX, double playerZ, float yaw)
    {
        if (_plan != plan) { _plan = plan; _span = plan.Profile.WorldSize; _cx = plan.Manifest.OriginX; _cz = plan.Manifest.OriginZ; }
        _playerX = playerX; _playerZ = playerZ; _playerYaw = yaw;
        Position = Vector2.Zero; Size = GetViewportRect().Size; // under a CanvasLayer, anchors alone leave it 0×0
        Visible = true;
        Redraw();
    }

    private void Redraw()
    {
        if (_plan == null) return;
        _image.Texture = ImageTexture.CreateFromImage(WorldPreview.Render(_plan, Pixels, _biomes, _cx, _cz, _span));
        _info.Text = $"World map — {(_biomes ? "biomes" : "terrain")} · {_span / 1000:0.#} km across\n" +
                     "Right-click: teleport there · wheel: zoom · left-drag: move · B: terrain / biomes · M or Esc: close";
        Layout();
    }

    private Rect2 MapRect()
    {
        float side = Mathf.Min(Size.X - 40, Size.Y - 70);
        return new Rect2((Size.X - side) / 2, Size.Y - side - 10, side, side);
    }

    private void Layout()
    {
        _info.Position = new Vector2(16, MapRect().Position.Y - 48);
        var r = MapRect();
        _image.Position = r.Position; _image.Size = r.Size;
        var p = ToScreen(_playerX, _playerZ);
        _marker.Visible = r.HasPoint(p);
        _marker.Size = new Vector2(30, 30);
        _marker.PivotOffset = new Vector2(15, 15);
        _marker.Position = p - new Vector2(15, 15);
        _marker.Rotation = -_playerYaw; // yaw 0 faces −Z (north, up on the map)
    }

    private Vector2 ToScreen(double x, double z)
    {
        var r = MapRect();
        return r.Position + new Vector2((float)((x - (_cx - _span / 2)) / _span), (float)((z - (_cz - _span / 2)) / _span)) * r.Size;
    }

    private (double X, double Z) ToWorld(Vector2 screen)
    {
        var r = MapRect();
        var f = (screen - r.Position) / r.Size;
        return (_cx - _span / 2 + f.X * _span, _cz - _span / 2 + f.Y * _span);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_plan == null) return;
        if (e is InputEventMouseButton b)
        {
            if (b.ButtonIndex == MouseButton.Left) _dragging = b.Pressed;
            if (!b.Pressed) return;
            if (b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var (wx, wz) = ToWorld(b.Position);
                double factor = b.ButtonIndex == MouseButton.WheelUp ? .5 : 2;
                double span = Math.Clamp(_span * factor, 2000, _plan.Profile.WorldSize);
                // Keep the point under the cursor in place.
                _cx = wx + (_cx - wx) * span / _span; _cz = wz + (_cz - wz) * span / _span;
                _span = span;
                Redraw();
            }
            if (b.ButtonIndex == MouseButton.Right && MapRect().HasPoint(b.Position))
            {
                var (wx, wz) = ToWorld(b.Position);
                Visible = false;
                Teleport?.Invoke(wx, wz);
            }
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion m && _dragging)
        {
            var r = MapRect();
            _cx -= m.Relative.X / r.Size.X * _span; _cz -= m.Relative.Y / r.Size.Y * _span;
            Layout();
            _pendingRedraw = true;
            AcceptEvent();
        }
    }

    private bool _pendingRedraw;
    private double _redrawTimer;

    public override void _Process(double delta)
    {
        if (!Visible || !_pendingRedraw) return;
        _redrawTimer += delta;
        if (_dragging && _redrawTimer < .25) return;
        _redrawTimer = 0; _pendingRedraw = false;
        Redraw();
    }

    public void ToggleBiomes() { _biomes = !_biomes; Redraw(); }
}
