using System;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Fishing in the game (GAMEPLAY_DESIGN §5b, Enshrouded style; D114): the rod held (7), the left mouse held to aim
/// farther and released to cast; the bobber must land in water; a bite after a while (quicker at dawn and dusk); click
/// when the bobber goes under; then the tug-of-war — hold A or D against the fish's pull, let go when it rests — until
/// it tires (landed: the host checks and adds it) or the line gives (lost, the bait with it). The fight runs on this PC
/// (FishFight, tested headless); the host has the last word on what lands in the pack.
/// </summary>
public partial class WorldLab
{
    private enum FishStage { Idle, Charging, Flying, Waiting, Bite, Fight }

    private FishStage _fishStage;
    private double _fishTimer, _castCharge, _fishFlight;
    private Vector3 _bobberFrom, _bobberAt;   // scene coordinates
    private double _bobberWorldX, _bobberWorldZ, _waterLevel, _castX, _castZ;
    private string _fishWater = "";
    private FishDef? _hooked;
    private FishFight? _fight;
    private MeshInstance3D? _bobber, _line;
    private ImmediateMesh? _lineMesh;
    private PanelContainer? _fishPanel;
    private ProgressBar? _fishBar, _lineBar;
    private Label? _fishLabel;
    private bool _lmbWasDown;
    private readonly Random _fishRng = new();
    private bool? _testLmb;   // --fish-test: the left mouse button held or not
    private int? _testPull;   // --fish-test: A (−1) or D (+1) or nothing

    /// <summary>While reeling in, A and D answer the fish (walking stops).</summary>
    private bool FishingHoldsMovement => _fishStage is FishStage.Fight or FishStage.Charging;

    private void ToggleRod()
    {
        if (!_me.RodHeld && !_myInv.HasTag("fishing")) { _status = "You have no fishing rod (make one: I — a branch and plant fibre)."; return; }
        if (_axeHeld) ToggleAxe();
        _me.RodHeld = !_me.RodHeld;
        StopFishing(null);
        if (_me.RodHeld) _tools.Select(0);
        _status = _me.RodHeld ? $"Rod in hand: face the water, hold and release the left mouse button to cast ({_myInv.CountTag("bait")} bait)." : "Rod put away.";
    }

    private void UpdateFishing(double delta)
    {
        bool lmb = Input.MouseMode == Input.MouseModeEnum.Captured && !_menu.Visible && !_pack.Visible && !_map.Visible && Input.IsMouseButtonPressed(MouseButton.Left);
        if (_testLmb is { } held) lmb = held; // (--fish-test presses the button)
        bool pressed = lmb && !_lmbWasDown, released = !lmb && _lmbWasDown;
        _lmbWasDown = lmb;
        if (!_me.RodHeld) { if (_fishStage != FishStage.Idle) StopFishing(null); return; }
        if (_fishStage is FishStage.Waiting or FishStage.Bite && new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() > 1.5f) { StopFishing("You reel in (you moved)."); return; }
        switch (_fishStage)
        {
            case FishStage.Idle:
                if (!pressed) break;
                if (!_myInv.HasTag("bait")) { _status = "No bait: make some from berries (I). Fish will not bite on a bare hook."; break; }
                _fishStage = FishStage.Charging; _castCharge = 0; _me.Fishing = true;
                break;
            case FishStage.Charging:
                _castCharge = Math.Min(1, _castCharge + delta / 1.2);
                _status = $"Casting… {new string('|', (int)(_castCharge * 20)),-20} (release to cast)";
                if (released) Cast();
                break;
            case FishStage.Flying:
                _fishFlight += delta / .7;
                var p = _bobberFrom.Lerp(_bobberAt, (float)Math.Min(1, _fishFlight));
                p.Y += Mathf.Sin(Mathf.Pi * (float)Math.Min(1, _fishFlight)) * 2.5f;
                PlaceBobber(p);
                if (_fishFlight < 1) break;
                if (_fishWater.Length == 0) { StopFishing("The bait lands on dry ground. Cast at the water."); break; }
                _fishStage = FishStage.Waiting;
                _fishTimer = Fishing.BiteDelay(_clock.Hour, _rain ? .5 : _atmosphere.Precipitation, _fishRng);
                _hooked = Fishing.Pick(_catalog.Fish, _fishWater, _clock.Hour, _fishRng);
                _status = $"Waiting for a bite… ({_fishWater}; click to reel in)";
                break;
            case FishStage.Waiting:
            {
                // A river's current carries the bobber slowly downstream (a few metres at most; never onto dry land).
                var current = Wakes.CurrentAt(_sampler!, _bobberWorldX, _bobberWorldZ) * .35f * (float)delta;
                double nx = _bobberWorldX + current.X, nz = _bobberWorldZ + current.Y;
                if (current.LengthSquared() > 0 && _sampler!.Sample(nx, nz).Water != WaterKind.None
                    && (nx - _castX) * (nx - _castX) + (nz - _castZ) * (nz - _castZ) < 4 * 4) { _bobberWorldX = nx; _bobberWorldZ = nz; }
                PlaceBobber(BobberRest(.02f * Mathf.Sin((float)_flickerFell * 2)));
            }
                if (pressed) { StopFishing("You reel in."); break; }
                if ((_fishTimer -= delta) > 0) break;
                if (_hooked == null) { StopFishing("Nothing bites here."); break; }
                _fishStage = FishStage.Bite; _fishTimer = .9;
                _status = "A bite! Click now!";
                break;
            case FishStage.Bite:
                // The bobber goes under, bobbing.
                PlaceBobber(BobberRest(-.18f + .06f * Mathf.Sin((float)_flickerFell * 25)));
                if (pressed)
                {
                    var rod = _myInv.Stacks.Select(s => _catalog.Item(s.Item)).FirstOrDefault(d => d.Has("fishing"));
                    _fight = new FishFight(_hooked!, rod?.Prop("strength", 1) ?? 1, rod?.Prop("endurance", 1) ?? 1, _fishRng);
                    _fishStage = FishStage.Fight;
                    ShowFishPanel(true);
                    break;
                }
                if ((_fishTimer -= delta) <= 0) StopFishing("Too slow — it took a nibble and swam off.");
                break;
            case FishStage.Fight:
            {
                int input = _testPull ?? (Input.IsKeyPressed(Key.A) ? -1 : 0) + (Input.IsKeyPressed(Key.D) ? 1 : 0);
                _fight!.Step(delta, input);
                // The bobber is dragged the way it pulls.
                var side = _camera.GlobalTransform.Basis.X with { Y = 0 };
                PlaceBobber(BobberRest(-.1f) + side.Normalized() * _fight.Pull * .5f * Mathf.Sin((float)_flickerFell * 3 + 1.2f));
                UpdateFishPanel();
                if (_fight.Landed)
                {
                    double kg = _hooked!.MinKg + (_hooked.MaxKg - _hooked.MinKg) * Math.Pow(_fishRng.NextDouble(), 1.8);
                    Act(new ActionRequest(ActionKind.Catch, 0, 0, _hooked.Id, kg, 0, 0, 0));
                    StopFishing(null);
                }
                else if (_fight.Lost)
                {
                    Act(new ActionRequest(ActionKind.Catch, 0, 0, "", 0, 0, 0, 0));
                    StopFishing("The line goes slack.");
                }
                break;
            }
        }
        DrawLine();
    }

    /// <summary>Release: the bobber flies 4–14 m where you look (farther the longer you held), into water or not.</summary>
    private void Cast()
    {
        var forward = -_camera.GlobalTransform.Basis.Z with { Y = 0 };
        forward = forward.Normalized();
        float distance = 4 + 10 * (float)_castCharge;
        _bobberWorldX = PlayerX + forward.X * distance; _bobberWorldZ = PlayerZ + forward.Z * distance;
        _castX = _bobberWorldX; _castZ = _bobberWorldZ;
        var t = _sampler!.Sample(_bobberWorldX, _bobberWorldZ);
        _fishWater = Fishing.WaterName(t.Water);
        _waterLevel = _fishWater.Length > 0 && !double.IsNaN(t.WaterLevel) ? t.WaterLevel : t.Height;
        _bobberFrom = _me.RodTip ?? _player.Position + Vector3.Up * 2;
        _bobberAt = new Vector3((float)(_bobberWorldX - _originX), (float)_waterLevel, (float)(_bobberWorldZ - _originZ));
        _fishFlight = 0;
        _fishStage = FishStage.Flying;
        _me.Facing = Mathf.Atan2(forward.X, forward.Z);
        if (_bobber == null)
        {
            _bobber = new MeshInstance3D { Mesh = new SphereMesh { Radius = .06f, Height = .12f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("d33a2a"), Roughness = .5f } };
            AddChild(_bobber);
        }
        _bobber.Visible = true;
    }

    /// <summary>The bobber on the surface as it is drawn — riding the waves of a lake or the sea (Tom), on a river's
    /// level — plus <paramref name="dy"/> (a bite pulls it under).</summary>
    private Vector3 BobberRest(float dy)
    {
        double level = WaterSurface(_bobberWorldX, _bobberWorldZ, out _);
        if (double.IsNaN(level)) level = _waterLevel;
        return new((float)(_bobberWorldX - _originX), (float)level + .03f + dy, (float)(_bobberWorldZ - _originZ));
    }

    private void PlaceBobber(Vector3 at) { if (_bobber != null) _bobber.Position = at; }

    /// <summary>The line from the rod's tip to the bobber (sagging a little while it waits).</summary>
    private void DrawLine()
    {
        bool show = _fishStage is FishStage.Flying or FishStage.Waiting or FishStage.Bite or FishStage.Fight && _bobber != null && _me.RodTip != null;
        if (_line == null)
        {
            _lineMesh = new ImmediateMesh();
            _line = new MeshInstance3D { Mesh = _lineMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.85f, .85f, .8f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
            AddChild(_line);
        }
        _lineMesh!.ClearSurfaces();
        _line.Visible = show;
        if (!show) return;
        var a = _me.RodTip!.Value; var b = _bobber!.Position;
        float sag = _fishStage == FishStage.Fight ? .05f : .6f;
        _lineMesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip);
        for (int i = 0; i <= 12; i++)
        {
            float k = i / 12f;
            _lineMesh.SurfaceAddVertex(a.Lerp(b, k) - Vector3.Up * sag * 4 * k * (1 - k));
        }
        _lineMesh.SurfaceEnd();
    }

    private void StopFishing(string? message)
    {
        _fishStage = FishStage.Idle; _fight = null; _hooked = null;
        _me.Fishing = false;
        if (_bobber != null) _bobber.Visible = false;
        if (_line != null) _line.Visible = false;
        ShowFishPanel(false);
        if (message != null) _status = message;
    }

    private void ShowFishPanel(bool show)
    {
        if (_fishPanel == null)
        {
            _fishPanel = new PanelContainer { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -230, OffsetRight = 230, OffsetTop = -330, OffsetBottom = -250 }; // (above the help lines)
            var box = new VBoxContainer();
            _fishPanel.AddChild(box);
            _fishLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            box.AddChild(_fishLabel);
            _fishBar = new ProgressBar { MaxValue = 1, Step = .001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14), Modulate = new Color(1, .45f, .4f) };
            _lineBar = new ProgressBar { MaxValue = 1, Step = .001, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14), Modulate = new Color(.5f, 1, .55f) };
            box.AddChild(new Label { Text = "Fish" }); box.AddChild(_fishBar);
            box.AddChild(new Label { Text = "Line" }); box.AddChild(_lineBar);
            _hud.GetParent().AddChild(_fishPanel);
        }
        _fishPanel.Visible = show;
        if (show) UpdateFishPanel();
    }

    private void UpdateFishPanel()
    {
        if (_fight == null || _fishBar == null || _lineBar == null || _fishLabel == null) return;
        _fishBar.Value = _fight.FishStamina; _lineBar.Value = _fight.Line;
        _fishLabel.Text = _fight.Pull switch
        {
            < 0 => "◀◀ it pulls LEFT — hold D",
            > 0 => "hold A — it pulls RIGHT ▶▶",
            _ => "it rests — let go",
        };
    }
}
