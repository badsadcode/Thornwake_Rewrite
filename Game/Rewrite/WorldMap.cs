using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// World map for exploring and testing (M): terrain or biome view (B), mouse wheel zooms at the cursor, left-drag pans,
/// right-click teleports there (onto the ground). An arrow shows the player and the way they look; in a multiplayer
/// game the others too (in their tunic colour, with their name), and a list on the right jumps to them.
/// </summary>
public partial class WorldMap : Control
{
    private const int Pixels = 768;
    private readonly TextureRect _image = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
    private readonly Control _markers = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _playerList = new();
    private string _listedNames = "";
    private readonly Label _info = new();
    private WorldPlan? _plan;
    private double _cx, _cz, _span;
    private bool _biomes, _dragging;
    private double _playerX, _playerZ; private float _playerYaw;

    /// <summary>Another player on the map: world position, yaw as the camera's (0 looks to −Z, north), tunic colour.</summary>
    public readonly record struct Other(int Id, string Name, double X, double Z, float Yaw, Color Color);

    /// <summary>Called with world (x, z) on right-click.</summary>
    public Action<double, double>? Teleport;
    /// <summary>The other players now (multiplayer), asked every frame while the map is open.</summary>
    public Func<IReadOnlyList<Other>>? Others;
    /// <summary>Called with a player's id from the list (go to them).</summary>
    public Action<int>? GoTo;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = new Color(0, 0, 0, .78f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });
        _image.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_image);
        _markers.SetAnchorsPreset(LayoutPreset.FullRect);
        _markers.Draw += DrawMarkers;
        AddChild(_markers);
        _playerList.AddThemeConstantOverride("separation", 4);
        AddChild(_playerList);
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
        _playerList.Position = new Vector2(r.End.X + 12, r.Position.Y);
        if (_playerList.Position.X + 170 > Size.X) _playerList.Position = new Vector2(r.End.X - 180, r.Position.Y + 8);
        _markers.QueueRedraw();
    }

    // An arrow, not a triangle: the notch at the back makes the way it points plain at a glance (Tom).
    private static readonly Vector2[] Arrow = { new(0, -14), new(10, 11), new(0, 5), new(-10, 11) };

    private void DrawArrow(Vector2 at, float yaw, Color fill, float scale)
    {
        // yaw 0 looks to −Z (north, up on the map); turning left (+yaw) turns the arrow anticlockwise.
        var points = Arrow.Select(v => at + v.Rotated(-yaw) * scale).ToArray();
        _markers.DrawColoredPolygon(points, fill);
        _markers.DrawPolyline(points.Append(points[0]).ToArray(), Colors.Black, 2.5f, true);
    }

    private void DrawMarkers()
    {
        var r = MapRect();
        var font = ThemeDB.FallbackFont;
        foreach (var o in Others?.Invoke() ?? Array.Empty<Other>())
        {
            var q = ToScreen(o.X, o.Z);
            if (!r.HasPoint(q)) continue;
            DrawArrow(q, o.Yaw, o.Color.Lightened(.25f), .85f);
            _markers.DrawString(font, q + new Vector2(13, -8), o.Name, HorizontalAlignment.Left, -1, 15, Colors.Black);
            _markers.DrawString(font, q + new Vector2(12, -9), o.Name, HorizontalAlignment.Left, -1, 15, Colors.White);
        }
        var p = ToScreen(_playerX, _playerZ);
        if (r.HasPoint(p)) DrawArrow(p, _playerYaw, new Color(1, .25f, .2f), 1);
    }

    private void UpdatePlayerList()
    {
        var others = Others?.Invoke() ?? Array.Empty<Other>();
        string names = string.Join("|", others.Select(o => $"{o.Id}:{o.Name}"));
        if (names == _listedNames) return;
        _listedNames = names;
        foreach (var child in _playerList.GetChildren()) child.QueueFree();
        if (others.Count == 0) return;
        _playerList.AddChild(new Label { Text = "Players" });
        foreach (var o in others)
        {
            int id = o.Id;
            var go = new Button { Text = "Go to " + o.Name, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(160, 0) };
            go.AddThemeColorOverride("font_color", o.Color.Lightened(.45f));
            go.Pressed += () => { Visible = false; GoTo?.Invoke(id); };
            _playerList.AddChild(go);
        }
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
        if (!Visible) return;
        UpdatePlayerList();
        _markers.QueueRedraw(); // the others move while the map is open
        if (!_pendingRedraw) return;
        _redrawTimer += delta;
        if (_dragging && _redrawTimer < .25) return;
        _redrawTimer = 0; _pendingRedraw = false;
        Redraw();
    }

    public void ToggleBiomes() { _biomes = !_biomes; Redraw(); }
}
