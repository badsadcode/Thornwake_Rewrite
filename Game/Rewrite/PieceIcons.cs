using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Gameplay;

namespace Thornwake.Rewrite;

/// <summary>
/// Pictures of the building pieces for the build menu (Tom, build 63: "a nice GUI with previews of the elements"):
/// each piece's own model rendered once, three-quarter view, on a transparent background, in a small off-screen viewport
/// with its own world and light — one piece every few frames, as the menu asks for them, then kept.
/// </summary>
public sealed partial class PieceIcons : Node
{
    public const int Size = 128;
    private readonly SubViewport _view;
    private readonly Camera3D _camera;
    private readonly Node3D _stage;
    private readonly Dictionary<string, Texture2D> _done = new();
    private readonly List<PieceDef> _queue = new();
    private readonly Func<PieceDef, Node3D?> _model;
    private PieceDef? _current;
    private int _wait;

    /// <summary>A picture became ready (the menu redraws).</summary>
    public event Action? Changed;

    public PieceIcons(Func<PieceDef, Node3D?> model)
    {
        Name = "PieceIcons";
        _model = model;
        _view = new SubViewport { Size = new Vector2I(Size, Size), TransparentBg = true, OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(_view);
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(.7f, .72f, .78f), AmbientLightEnergy = 1.5f, TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 1.35f };
        _view.AddChild(new WorldEnvironment { Environment = env });
        _view.AddChild(new DirectionalLight3D { Rotation = new Vector3(-.9f, .6f, 0), LightEnergy = 2.2f, LightColor = new Color(1, .96f, .9f) });
        _view.AddChild(new DirectionalLight3D { Rotation = new Vector3(-.4f, 2.6f, 0), LightEnergy = .8f, LightColor = new Color(.8f, .86f, 1f) }); // (a fill from behind)
        _camera = new Camera3D { Fov = 30, Current = true, Near = .05f, Far = 200 };
        _view.AddChild(_camera);
        _stage = new Node3D { Name = "Stage" };
        _view.AddChild(_stage);
    }

    /// <summary>The piece's picture, or null while it is still being made (asked for now).</summary>
    public Texture2D? Get(PieceDef def)
    {
        if (_done.TryGetValue(def.Id, out var t)) return t;
        if (_current != def && !_queue.Contains(def)) _queue.Add(def);
        return null;
    }

    public override void _Process(double delta)
    {
        if (_current == null)
        {
            if (_queue.Count == 0) return;
            _current = _queue[0];
            _queue.RemoveAt(0);
            var model = _model(_current) ?? new Node3D();
            GameplayModels.HideCollisionMeshes(model);
            foreach (var light in model.FindChildren("*", "Light3D", true, false)) ((Node3D)light).Visible = false;
            _stage.AddChild(model);
            Frame(model, _current.Category == "Camp");
            _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            _wait = 2; // (drawn at the end of this frame; read back after the next)
            return;
        }
        if (--_wait > 0) return;
        var image = _view.GetTexture().GetImage();
        _done[_current.Id] = ImageTexture.CreateFromImage(image);
        foreach (var child in _stage.GetChildren()) child.QueueFree();
        _current = null;
        Changed?.Invoke();
    }

    /// <summary>The camera three-quarters in front and above, the whole model in view.</summary>
    private void Frame(Node3D model, bool front)
    {
        var box = new Aabb();
        bool first = true;
        foreach (var child in model.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mi && mi.Visible && mi.Mesh != null)
            {
                var b = mi.GlobalTransform * mi.Mesh.GetAabb();
                box = first ? b : box.Merge(b);
                first = false;
            }
        if (first) box = new Aabb(new Vector3(-1, 0, -1), new Vector3(2, 2, 2));
        var centre = box.GetCenter();
        float radius = Mathf.Max(box.Size.Length() / 2, .3f);
        float dist = radius / Mathf.Sin(Mathf.DegToRad(_camera.Fov / 2)) * 1.02f;
        // (from behind the piece's −Z side: a roof's upper face and a stair's treads face it; camp pieces open to +Z)
        var dir = (front ? new Vector3(.75f, .62f, 1.1f) : new Vector3(.8f, .75f, -1f)).Normalized();
        _camera.Position = centre + dir * dist;
        _camera.LookAt(centre, Vector3.Up);
    }
}
