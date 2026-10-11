using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Collision test (round 30c; Tom, build 44: "when I'm walking towards a rock and I hit it, I get rubber-banded back …
/// I'm able to go up on high slopes … collisions will be important, we'll have climbing eventually"). Runs the real
/// player through the real streamed collision and checks the behaviour, so a change that breaks it cannot pass:
/// * <b>trunk</b> and <b>rock</b>: walk, then sprint, straight at a free-standing tree / boulder from 5 m. The player
///   must stop at it (never inside it) and, while still pushing forward, never be pushed back by more than 0.2 m.
/// * <b>slope</b>: walk up the steepest ground nearby (steeper than the 50° the player may climb). The player must not
///   gain more than 1.5 m of height in 5 s.
/// <c>--collision-test</c> (with any world arguments, e.g. a debug dump's Reproduce line; runs headless). Prints
/// COLLISION_TEST lines and quits with 0 when every check passes. Part of the checks before each build.
/// </summary>
public partial class WorldLab
{
    private sealed class CollisionCase
    {
        public required string Name;
        public required Vector3 Target;     // world position of the obstacle (x, z) or the slope's top (y used for slope)
        public required Vector3 Start;      // world start (x, z)
        public float Radius;                // expected stop distance from the target's axis (trunk/rock radius + player radius)
        public bool Slope, Stand, Jump, Land;
        public bool Sprint;
        public double Time;
        public float StartY, MinDistance = float.MaxValue, Contact = -1, PushBack, MaxRise, MaxFloorAngle;
        public string Result = "";
    }

    private List<CollisionCase>? _collisionCases;
    private int _collisionIndex;
    private int _collisionStage;
    private Vector2 _testInput;
    private bool _testSprint, _testJump;

    private bool CollisionTestActive => _collisionCases != null;

    // Eight directions to approach an obstacle from (any two clear ones make a walk and a sprint case).
    private static readonly Vector3[] Approaches = Enumerable.Range(0, 8).Select(i => new Vector3(Mathf.Cos(i * Mathf.Pi / 4), 0, Mathf.Sin(i * Mathf.Pi / 4))).ToArray();

    /// <summary>Find the test sites around the player once the terrain has settled.</summary>
    private void CollisionTestSetup()
    {
        var cases = new List<CollisionCase>();
        var config = DetailConfig.For(_sampler!);
        double px = PlayerX, pz = PlayerZ;
        long tx = (long)Math.Floor(px / TileBuilder.BaseTile), tz = (long)Math.Floor(pz / TileBuilder.BaseTile);
        var trees = new List<TreeInstance>();
        var rocks = new List<(Vector3 At, float Size, int Kind)>();
        var solids = new List<Vector3>(); // everything with collision (trees, rocks, logs, stumps, bushes): walk lines must be clear
        for (long z = tz - 3; z <= tz + 3; z++)
            for (long x = tx - 3; x <= tx + 3; x++)
            {
                var key = new TileKey(0, x, z);
                var data = TileBuilder.Build(_sampler!, key, config);
                var origin = new Vector3((float)key.MinX, 0, (float)key.MinZ);
                trees.AddRange(data.Trees.Select(t => new TreeInstance(t.Species, t.Transform.Translated(origin))));
                solids.AddRange(data.Trees.Where(t => ObjectCollision.TrunkRadius(t.Species) > 0).Select(t => t.Transform.Origin + origin));
                solids.AddRange(data.Props.Select(p => p.Transform.Origin + origin));
                solids.AddRange(data.Rocks.Where(r => r.Kind is not (GroundCover.Stone or GroundCover.CliffSlab)).Select(r => r.Transform.Origin + origin));
                foreach (var r in data.Rocks)
                {
                    if (r.Kind is GroundCover.Stone or GroundCover.CliffSlab) continue;
                    var b = r.Transform.Basis;
                    float size = (b.X.Length() + b.Y.Length() + b.Z.Length()) / 3;
                    // Rocks are partly buried: only boulders standing at least 0.6 m out of the ground are obstacles.
                    float rockTop = RockMeshes.HullPoints(r.Kind).Max(p => (b * p).Y) + r.Transform.Origin.Y;
                    var at = r.Transform.Origin + origin;
                    if (size >= .9f && rockTop - _sampler!.Height(at.X, at.Z, 1) >= .6f) rocks.Add((at, size, r.Kind));
                }
            }
        // A tree with nothing else within 6 m, on gentle ground, approached from open ground.
        foreach (var t in trees.OrderBy(t => Dist2(t.Transform.Origin, px, pz)))
        {
            float radius = ObjectCollision.TrunkRadius(t.Species) * t.Transform.Basis.X.Length();
            if (radius <= .1f) continue;
            var at = t.Transform.Origin;
            if (trees.Any(o => o.Transform.Origin != at && Flat(o.Transform.Origin, at) < 6) || rocks.Any(r => Flat(r.At, at) < 6)) continue;
            var starts = Approaches
                .Select(d => at + d * 5).Where(p => !Steep(at, p) && Dry(p) && Clear(p, at)).ToList();
            if (starts.Count < 2) continue;
            cases.Add(new CollisionCase { Name = "trunk walk", Target = at, Start = starts[0], Radius = radius + .3f });
            cases.Add(new CollisionCase { Name = "trunk sprint", Target = at, Start = starts[1], Radius = radius + .3f, Sprint = true });
            break;
        }
        int rIsolated = 0, rGentle = 0, rClear = 0, rDry = 0;
        foreach (var r in rocks)
        {
            if (trees.Any(o => Flat(o.Transform.Origin, r.At) < 4) || rocks.Any(o => o.At != r.At && Flat(o.At, r.At) < 4)) continue;
            rIsolated++;
            var ss = Approaches.Select(d => r.At + d * (5 + r.Size)).ToList();
            if (ss.Count(p => Dry(p)) >= 2) rDry++;
            if (ss.Count(p => Dry(p) && !Steep(r.At, p)) >= 2) rGentle++;
            if (ss.Count(p => Dry(p) && !Steep(r.At, p) && Clear(p, r.At)) >= 2) rClear++;
        }
        GD.Print($"COLLISION_TEST rock candidates: {rocks.Count} boulders standing out, {rIsolated} isolated, {rDry} dry, {rGentle} gentle, {rClear} clear");
        foreach (var r in rocks.OrderBy(r => Dist2(r.At, px, pz)))
        {
            if (trees.Any(o => Flat(o.Transform.Origin, r.At) < 4) || rocks.Any(o => o.At != r.At && Flat(o.At, r.At) < 4)) continue;
            var starts = Approaches
                .Select(d => r.At + d * (5 + r.Size)).Where(p => !Steep(r.At, p) && Dry(p) && Clear(p, r.At)).ToList();
            if (starts.Count < 2) continue;
            cases.Add(new CollisionCase { Name = "rock walk", Target = r.At, Start = starts[0], Radius = .3f });
            cases.Add(new CollisionCase { Name = "rock sprint", Target = r.At, Start = starts[1], Radius = .3f, Sprint = true });
            break;
        }
        // The steepest slope within ~150 m: compare heights 3 m apart on a 10 m grid.
        double best = 0; Vector3 bottom = default, top = default;
        for (double z = pz - 150; z <= pz + 150; z += 10)
            for (double x = px - 150; x <= px + 150; x += 10)
                for (int d = 0; d < 8; d++)
                {
                    double a = d * Math.PI / 4, dx = Math.Cos(a) * 3, dz = Math.Sin(a) * 3;
                    var s0 = _sampler!.Sample(x, z);
                    if (s0.Water != WaterKind.None) continue;
                    double rise = _sampler.Height(x + dx, z + dz, 1) - s0.Height;
                    if (rise / 3 > best) { best = rise / 3; bottom = new Vector3((float)x, 0, (float)z); top = new Vector3((float)(x + dx * 3), 0, (float)(z + dz * 3)); }
                }
        double angle = Math.Atan(best) * 180 / Math.PI;
        if (angle > 58) cases.Add(new CollisionCase { Name = $"slope {angle:0}°", Target = top, Start = bottom, Slope = true });
        else GD.Print($"COLLISION_TEST slope SKIPPED (steepest ground nearby {angle:0}°, needs > 58°)");
        // A cliff step: the terrain drops more than 3 m between two 1 m grid points, where the collision mesh ramps
        // metres above the exact height. Landing there (teleport, map) must put the player on the ground, not under it.
        {
            bool found = false;
            for (double z = Math.Floor(pz) - 100; z <= pz + 100 && !found; z += 2)
                for (double x = Math.Floor(px) - 100; x <= px + 100 && !found; x++)
                {
                    var a = _sampler!.Sample(x, z); var b = _sampler.Sample(x + 1, z);
                    if (a.Water != WaterKind.None || b.Water != WaterKind.None || Math.Abs(a.Height - b.Height) < 3) continue;
                    double mid = _sampler.Sample(x + .5, z).Height, ramp = (a.Height + b.Height) / 2;
                    if (Math.Abs(mid - ramp) < 1.5) continue; // the step must fall between the grid points
                    cases.Add(new CollisionCase { Name = $"cliff landing ({Math.Abs(a.Height - b.Height):0.0} m step)", Target = new Vector3((float)x + 1, 0, (float)z), Start = new Vector3((float)x + .5f, 0, (float)z), Land = true });
                    found = true;
                }
            if (!found) GD.Print("COLLISION_TEST cliff landing SKIPPED (no cliff step within 100 m)");
        }
        if (!cases.Any(c => c.Name.StartsWith("trunk"))) GD.Print("COLLISION_TEST trunk SKIPPED (no free-standing tree nearby)");
        if (!cases.Any(c => c.Name.StartsWith("rock"))) GD.Print("COLLISION_TEST rock SKIPPED (no free-standing boulder nearby)");
        // Standing still on open ground: the player must rest, not jitter (Tom, build 44).
        // Standing and jumping on dry, gentle ground nearest the start (the start may be in a lake).
        var dry = new Vector3((float)px, 0, (float)pz);
        for (double r = 0; r <= 200 && (!Dry(dry) || Steep(dry, dry + new Vector3(1, 0, 0))); r += 4)
            for (int a = 0; a < 16; a++)
            {
                var p = new Vector3((float)(px + Math.Cos(a * Math.PI / 8) * r), 0, (float)(pz + Math.Sin(a * Math.PI / 8) * r));
                if (Dry(p) && !Steep(p, p + new Vector3(1, 0, 0))) { dry = p; break; }
            }
        px = dry.X; pz = dry.Z;
        if (Dry(dry))
        {
            cases.Insert(0, new CollisionCase { Name = "jump", Target = new Vector3((float)px + 1, 0, (float)pz), Start = new Vector3((float)px, 0, (float)pz), Jump = true });
            cases.Insert(0, new CollisionCase { Name = "standing still", Target = new Vector3((float)px + 1, 0, (float)pz), Start = new Vector3((float)px, 0, (float)pz), Stand = true });
        }
        else GD.Print("COLLISION_TEST standing/jump SKIPPED (no dry ground within 200 m)");
        if (cases.Count == 0) { GD.Print("COLLISION_TEST_FAIL (nothing to test here)"); GetTree().Quit(1); return; }
        _collisionCases = cases;
        _collisionIndex = 0; _collisionStage = 0;
        // Every rock kind's hull on its own, high above the world: the physics engine must see each one.
        for (int k = 0; k < 5; k++)
        {
            var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0, Name = $"HullProbe{k}", Position = new Vector3(k * 10, 3000, 0) };
            var unit = RockMeshes.HullPoints(k);
            body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = unit.Select(p => p * 3).ToArray() } });
            AddChild(body);
        }

        static double Dist2(Vector3 p, double x, double z) => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z);
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
        bool Dry(Vector3 p) => _sampler!.Sample(p.X, p.Z).Water == WaterKind.None;
        // Nothing solid but the target within 1.2 m of the walk line (start to 1.5 m short of the target).
        bool Clear(Vector3 start, Vector3 target) => !solids.Any(o =>
        {
            if (Flat(o, target) < .05f) return false;
            var a = new Vector2(start.X, start.Z); var ab = new Vector2(target.X, target.Z) - a;
            float u = Math.Clamp((new Vector2(o.X, o.Z) - a).Dot(ab) / ab.LengthSquared(), 0, 1 - 1.5f / ab.Length());
            return (a + ab * u - new Vector2(o.X, o.Z)).Length() < 1.2f;
        });
        // Steep (a = target, b = start): more than 2.5 m between the ends; the landing spot steeper than 40° (the player
        // would slide off it); or any metre of the walk up to 1.5 m short of the target steeper than 45° (the player walks
        // up to 50°). Checked on the exact height and on the 1 m collision grid cells around each point.
        bool Steep(Vector3 a, Vector3 b)
        {
            double H(double x, double z) => _sampler!.Sample(x, z).Height; // what Teleport and the tile mesh use
            if (Math.Abs(H(a.X, a.Z) - H(b.X, b.Z)) > 2.5) return true;
            float length = Flat(a, b);
            int n = Math.Max(1, (int)Math.Ceiling(length));
            for (int i = 0; i <= n; i++)
            {
                if (i > 0 && length * i / n > length - 1.5f) break;
                double limit = i == 0 ? .84 : 1.0; // tan 40°, tan 45°
                double x = b.X + (a.X - b.X) * i / n, z = b.Z + (a.Z - b.Z) * i / n;
                double gx = (H(x + .5, z) - H(x - .5, z)), gz = (H(x, z + .5) - H(x, z - .5));
                if (Math.Sqrt(gx * gx + gz * gz) > limit) return true;
                double cx = Math.Floor(x), cz = Math.Floor(z);
                for (int v = -1; v <= 1; v++)
                    for (int u = -1; u <= 1; u++)
                    {
                        double h0 = H(cx + u, cz + v);
                        if (Math.Abs(H(cx + u + 1, cz + v) - h0) > limit || Math.Abs(H(cx + u, cz + v + 1) - h0) > limit) return true;
                    }
            }
            return false;
        }
    }

    /// <summary>Every physics frame while the test runs: steer the player and record what happens.</summary>
    private void CollisionTestStep(double delta)
    {
        if (_collisionCases == null) return;
        if (_collisionIndex >= _collisionCases.Count) { CollisionTestFinish(); return; }
        var c = _collisionCases[_collisionIndex];
        var here = new Vector3((float)PlayerX, _player.Position.Y, (float)PlayerZ);
        switch (_collisionStage)
        {
            case 0: // place the player at the start, facing the target, and wait for the ground (collision) there
                Teleport(c.Start.X, c.Start.Z);
                SetFly(false);
                _yaw = Mathf.Atan2(-(c.Target.X - c.Start.X), -(c.Target.Z - c.Start.Z));
                _testInput = Vector2.Zero; _testSprint = c.Sprint;
                c.Time = 0;
                _collisionStage = 1;
                break;
            case 1:
                if (OS.HasFeature("debug") && System.Environment.GetEnvironmentVariable("CT_TRACE") == "1")
                    GD.Print($"CT_TRACE {c.Name} t {c.Time:0.00} off {new Vector2(here.X - c.Start.X, here.Z - c.Start.Z).Length():0.00} pos {_player.Position} vel {_player.Velocity} floor {_player.IsOnFloor()} holding {_holding} origin {_originX},{_originZ}");
                c.Time += delta;
                if (!_holding && _player.IsOnFloor() && c.Time > .5 && _streamer.PendingCollision == 0)
                {
                    float off = new Vector2(here.X - c.Start.X, here.Z - c.Start.Z).Length();
                    if (c.Land) { c.Result = $"PASS (standing on the ground, feet {_player.Position.Y - _sampler!.Sample(here.X, here.Z).Height:0.00} m above the exact height)"; Next(); break; }
                    if (off > .5f && !c.Slope) { c.Result = $"FAIL (landed {off:0.00} m away from the start: pushed out of something)"; Next(); break; }
                    c.StartY = _player.Position.Y; c.Time = 0; _collisionStage = 2;
                }
                else if (c.Time > 30)
                {
                    var t = _sampler!.Sample(here.X, here.Z);
                    var fromAbove = _player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(_player.GlobalPosition + Vector3.Up * 300, _player.GlobalPosition + Vector3.Down * 300, 1));
                    string surface = fromAbove.Count > 0 ? $"collision surface {((Vector3)fromAbove["position"]).Y - _player.Position.Y:0.00} m above the feet" : "no collision below or above";
                    c.Result = $"FAIL (never stood on the ground at the start: holding {_holding}, on floor {_player.IsOnFloor()}, feet {_player.Position.Y - t.Height:0.00} m above the ground, {surface}, water {t.Water}, swimming {_swimming})";
                    Next();
                }
                break;
            case 2 when c.Jump:
            {
                c.Time += delta;
                _testJump = c.Time < .15;
                c.MaxRise = Math.Max(c.MaxRise, _player.Position.Y - c.StartY);
                if (c.Time > 1.5)
                {
                    _testJump = false;
                    c.Result = c.MaxRise > .7f ? $"PASS (jumped {c.MaxRise:0.00} m)" : $"FAIL (could not jump: rose {c.MaxRise:0.00} m)";
                    Next();
                }
                break;
            }
            case 2 when c.Stand:
            {
                c.Time += delta;
                if (c.Time < .5) { c.MinDistance = _player.Position.Y; c.MaxRise = _player.Position.Y; break; }
                c.MinDistance = Math.Min(c.MinDistance, _player.Position.Y);
                c.MaxRise = Math.Max(c.MaxRise, _player.Position.Y);
                c.PushBack = Math.Max(c.PushBack, new Vector2(here.X - c.Start.X, here.Z - c.Start.Z).Length());
                if (c.Time > 3.5)
                {
                    float jitter = c.MaxRise - c.MinDistance;
                    c.Result = jitter <= .02f && c.PushBack < .3f ? $"PASS (moved {jitter * 100:0.0} cm up/down)" : $"FAIL (jittered {jitter * 100:0.0} cm up/down, drifted {c.PushBack:0.00} m)";
                    Next();
                }
                break;
            }
            case 2: // walk / sprint at it
            {
                c.Time += delta;
                _yaw = Mathf.Atan2(-(c.Target.X - here.X), -(c.Target.Z - here.Z)); // keep heading at the target
                _testInput = new Vector2(0, -1);
                float d = new Vector2(here.X - c.Target.X, here.Z - c.Target.Z).Length();
                if (!c.Slope)
                {
                    // Height above the local ground (the walk may run downhill).
                    float above = _player.Position.Y - (float)_sampler!.Sample(here.X, here.Z).Height;
                    c.MaxRise = Math.Max(c.MaxRise, above);
                    if (_player.IsOnFloor() && above > .2f) c.MaxFloorAngle = Math.Max(c.MaxFloorAngle, Mathf.RadToDeg(_player.GetFloorAngle()));
                    c.MinDistance = Math.Min(c.MinDistance, d);
                    // In contact once the player stops getting closer.
                    if (c.Contact < 0 && _player.GetRealVelocity().Length() < 1 && c.Time > .4) c.Contact = d;
                    if (c.Contact >= 0) c.PushBack = Math.Max(c.PushBack, d - c.MinDistance);
                }
                else c.MaxRise = Math.Max(c.MaxRise, _player.Position.Y - c.StartY);
                if (c.Time > (c.Slope ? 5 : 3))
                {
                    _testInput = Vector2.Zero;
                    if (c.Slope) c.Result = c.MaxRise <= 1.5 ? $"PASS (rose {c.MaxRise:0.00} m)" : $"FAIL (climbed {c.MaxRise:0.00} m up a slope steeper than 50°)";
                    // Walking up a boulder's side is allowed where it is no steeper than any walkable slope (50°).
                    else if (c.Contact < 0 && c.MaxRise > .4f && c.Name.StartsWith("rock") && c.MaxFloorAngle <= 51)
                        c.Result = $"PASS (walked up its gentle side like a slope: stood {c.MaxRise:0.00} m above the ground, steepest footing {c.MaxFloorAngle:0}°)";
                    else if (c.Contact < 0 && c.MaxRise > .4f) c.Result = $"FAIL (walked up and over it: stood {c.MaxRise:0.00} m above the ground, steepest footing {c.MaxFloorAngle:0}°)";
                    else if (c.Contact < 0) c.Result = $"FAIL (walked through it, closest {c.MinDistance:0.00} m, at most {c.MaxRise:0.00} m above the ground; {WhatIsAt(c.Target)})";
                    else if (c.MinDistance < c.Radius - .15f && c.Name.StartsWith("trunk")) c.Result = $"FAIL (went into the trunk: {c.MinDistance:0.00} m from its axis)";
                    else if (c.PushBack > .2f) c.Result = $"FAIL (pushed back {c.PushBack:0.00} m while pushing against it)";
                    else c.Result = $"PASS (stopped {c.MinDistance:0.00} m from its centre, pushed back {c.PushBack:0.00} m)";
                    Next();
                }
                break;
            }
        }

        void Next()
        {
            GD.Print($"COLLISION_TEST {c.Name}: {c.Result}");
            _collisionIndex++; _collisionStage = 0; _testInput = Vector2.Zero; _testSprint = false; _testJump = false;
        }
    }

    /// <summary>What the physics engine has straight down through a world position (for failure reports).</summary>
    private string WhatIsAt(Vector3 world)
    {
        var at = new Vector3((float)(world.X - _originX), world.Y, (float)(world.Z - _originZ));
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 50, at + Vector3.Down * 5, 1));
        if (hit.Count == 0) return "nothing solid there";
        var owner = hit["collider"].As<CollisionObject3D>();
        var shape = owner?.ShapeOwnerGetOwner(owner.ShapeFindOwner(hit["shape"].AsInt32())) as CollisionShape3D;
        return $"top of {shape?.Shape?.GetType().Name ?? "?"} at {((Vector3)hit["position"]).Y - world.Y:0.00} m above the rock's origin, ground {_sampler!.Sample(world.X, world.Z).Height - world.Y:0.00} m";
    }

    private void CollisionTestFinish()
    {
        bool ok = _collisionCases!.All(c => c.Result.StartsWith("PASS"));
        for (int k = 0; k < 5; k++)
        {
            var at = new Vector3(k * 10, 3000, 0);
            var hits = _player.GetWorld3D().DirectSpaceState.IntersectPoint(new PhysicsPointQueryParameters3D { Position = at + Vector3.Up * .1f, CollisionMask = 1 }, 4);
            var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + new Vector3(0, .2f, 5), at + new Vector3(0, .2f, -5), 1));
            bool seen = hits.Count > 0 && hit.Count > 0;
            GD.Print($"COLLISION_TEST rock hull kind {k} ({RockMeshes.HullPoints(k).Length} points): {(seen ? "PASS" : "FAIL")} (point query {hits.Count}, ray {(hit.Count > 0 ? "hit at z " + ((Vector3)hit["position"]).Z.ToString("0.00") : "missed")})");
            ok &= seen;
        }
        GD.Print($"COLLISION_TEST_{(ok ? "PASS" : "FAIL")} ({_collisionCases!.Count} checks)");
        _collisionCases = null;
        GetTree().Quit(ok ? 0 : 1);
    }
}
