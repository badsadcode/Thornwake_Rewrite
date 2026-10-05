using System;
using Godot;
using Thornwake.WorldGen;
using Noise = Thornwake.WorldGen.Noise;

namespace Thornwake.Rewrite;

/// <summary>
/// Ground mist wisps around the player (Settings → Ground fog and mist): soft sheets anchored to the world on a 16 m
/// grid, hugging the ground (or water), denser in hollows below the player. Works on any GPU (no volumetric fog
/// needed); the amount comes from <see cref="Atmosphere.MistAmount"/> (dawn, misty biomes, rain, water nearby).
/// </summary>
public partial class MistField : Node3D
{
    private const double Cell = 16, Radius = 150;
    private readonly MultiMeshInstance3D _node = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly ShaderMaterial _material = new() { Shader = GD.Load<Shader>("res://Game/Rewrite/MistWisp.gdshader") };
    private double _builtX = double.NaN, _builtZ, _builtOriginX, _builtOriginZ;

    public override void _Ready()
    {
        _node.MaterialOverride = _material;
        _node.Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = new QuadMesh { Size = new Vector2(1, 1), CenterOffset = new Vector3(0, .5f, 0) } };
        AddChild(_node);
    }

    public void Update(TerrainSampler sampler, double playerX, double playerZ, double originX, double originZ, float amount, float playerGround, Color colour)
    {
        Visible = amount > .02f;
        _material.SetShaderParameter("amount", amount);
        _material.SetShaderParameter("mist_color", colour);
        if (!Visible) return;
        if (!double.IsNaN(_builtX) && Math.Abs(playerX - _builtX) < 12 && Math.Abs(playerZ - _builtZ) < 12 && originX == _builtOriginX && originZ == _builtOriginZ) return;
        _builtX = playerX; _builtZ = playerZ; _builtOriginX = originX; _builtOriginZ = originZ;
        var transforms = new System.Collections.Generic.List<(Transform3D T, float Opacity, float Seed)>();
        long c0x = (long)Math.Floor((playerX - Radius) / Cell), c1x = (long)Math.Floor((playerX + Radius) / Cell);
        long c0z = (long)Math.Floor((playerZ - Radius) / Cell), c1z = (long)Math.Floor((playerZ + Radius) / Cell);
        for (long cz = c0z; cz <= c1z; cz++)
            for (long cx = c0x; cx <= c1x; cx++)
            {
                ulong h = Hashing.Lattice(cx, cz, 0x3157UL);
                // Patches: a slow world noise decides where mist lies (and where it is clear).
                double x = (cx + .2 + .6 * Hashing.Unit(h)) * Cell, z = (cz + .2 + .6 * Hashing.Unit(Hashing.Mix(h))) * Cell;
                double d = Math.Sqrt((x - playerX) * (x - playerX) + (z - playerZ) * (z - playerZ));
                if (d > Radius) continue;
                double patch = .5 + .5 * Noise.Value(x / 140, z / 140, 0x5A5AUL);
                var t = sampler.Sample(x, z, 8);
                double surface = double.IsNaN(t.WaterLevel) ? t.Height : Math.Max(t.Height, t.WaterLevel);
                double hollow = Math.Clamp((playerGround - surface) / 12, 0, 1);
                double keep = patch + .35 * hollow + (t.Water != WaterKind.None ? .25 : 0);
                if (keep < .55) continue;
                float size = (float)(14 + 14 * Hashing.Unit(Hashing.Mix(h + 3)));
                var basis = Basis.Identity.Scaled(new Vector3(size, size, size));
                var origin = new Vector3((float)(x - originX), (float)(surface + .6), (float)(z - originZ));
                transforms.Add((new Transform3D(basis, origin), (float)Math.Clamp(.35 + .9 * (keep - .55) + .3 * hollow, 0, 1), (float)Hashing.Unit(Hashing.Mix(h + 7))));
            }
        var mm = _node.Multimesh;
        mm.InstanceCount = transforms.Count;
        for (int i = 0; i < transforms.Count; i++)
        {
            mm.SetInstanceTransform(i, transforms[i].T);
            mm.SetInstanceCustomData(i, new Color(transforms[i].Opacity, transforms[i].Seed, 0, 0));
        }
        // Huge cull box: instances are placed in world (scene) coordinates around the player.
        _node.CustomAabb = new Aabb(new Vector3((float)(playerX - originX - Radius - 40), -2000, (float)(playerZ - originZ - Radius - 40)), new Vector3((float)Radius * 2 + 80, 12000, (float)Radius * 2 + 80));
    }
}
