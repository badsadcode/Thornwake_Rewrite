using System;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>One plant sprite instance (tile-local transform; the sprite quad is 1 m, scaled to the plant's size).</summary>
public readonly record struct FloraInstance(int Species, Transform3D Transform, Color Tint);

/// <summary>
/// Pixel-art plant sprites (round 22, Noor and Tom's packs, Assets/Flora: 256 px, cropped to the plant, bottom-aligned)
/// as one texture array, drawn as crossed quads (upright plants) or flat quads (floating leaves) with Flora.gdshader.
/// Where they grow: GroundCover.Flora (vegetation as populations, docs/design-inbox/NOOR_VEGETATION_POPULATIONS.md).
/// </summary>
public static class Flora
{
    public enum Form { Upright, Floating, Underwater }

    /// <summary>A sprite: file, height range in metres, how it is drawn, how much it sways.</summary>
    public readonly record struct Species(string File, float MinHeight, float MaxHeight, Form Form);

    // Indices into All (= texture array layers).
    public const int ShortA = 0, ShortB = 1, MediumA = 2, MediumB = 3, TallA = 4, TallB = 5, TallC = 6, TallD = 7, Lush = 8,
        Seeded = 9, WheatYoung = 10, WheatGolden = 11,
        ReedsCommon = 12, ReedsRush = 13, Cattails = 14, Flowering = 15,
        LilyPads = 16, WaterLilies = 17, Duckweed = 18,
        Submerged = 19, Seagrass = 20, EelgrassTall = 21, Broadleaf = 22, Feathery = 23, Mixed = 24, GreenAlgae = 25, RedAlgae = 26, Kelp = 27;

    public static readonly Species[] All =
    {
        new("grass_short_a", .45f, .7f, Form.Upright), new("grass_short_b", .45f, .7f, Form.Upright),
        new("grass_medium_a", .6f, .9f, Form.Upright), new("grass_medium_b", .6f, .9f, Form.Upright),
        new("grass_tall_a", .8f, 1.15f, Form.Upright), new("grass_tall_b", .8f, 1.15f, Form.Upright),
        new("grass_tall_c", .85f, 1.2f, Form.Upright), new("grass_tall_d", .85f, 1.25f, Form.Upright),
        new("grass_lush", .7f, 1.05f, Form.Upright), new("grass_seeded", .9f, 1.25f, Form.Upright),
        new("wheat_young", .9f, 1.2f, Form.Upright), new("wheat_golden", .95f, 1.25f, Form.Upright),
        new("reeds_common", 1.9f, 2.6f, Form.Upright), new("reeds_rush", 1.3f, 1.8f, Form.Upright),
        new("cattails", 1.7f, 2.3f, Form.Upright), new("flowering_aquatic", .7f, 1.05f, Form.Upright),
        new("lily_pads", 1f, 1.8f, Form.Floating), new("water_lilies", 1f, 1.6f, Form.Floating), new("duckweed", 1.2f, 2.2f, Form.Floating),
        new("submerged_plant", .5f, 1.1f, Form.Underwater), new("seagrass", .5f, .9f, Form.Underwater),
        new("eelgrass_tall", .8f, 1.6f, Form.Underwater), new("broadleaf_aquatic", .6f, 1.1f, Form.Underwater),
        new("feathery_aquatic", .6f, 1.2f, Form.Underwater), new("mixed_aquatic", .6f, 1.1f, Form.Underwater),
        new("green_algae", .4f, .8f, Form.Underwater), new("red_algae", .5f, 1f, Form.Underwater), new("kelp", 2.5f, 5.5f, Form.Underwater),
    };

    private static Texture2DArray? _array;
    private static ArrayMesh? _upright, _flat;
    private static ShaderMaterial? _material;

    public static Texture2DArray Textures()
    {
        if (_array != null) return _array;
        var images = new Godot.Collections.Array<Image>();
        foreach (var s in All)
        {
            var image = GD.Load<Texture2D>($"res://Assets/Flora/{s.File}.png").GetImage();
            if (image.IsCompressed()) image.Decompress();
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != 256) image.Resize(256, 256, Image.Interpolation.Lanczos);
            image.GenerateMipmaps();
            images.Add(image);
        }
        _array = new Texture2DArray();
        _array.CreateFromImages(images);
        return _array;
    }

    public static ShaderMaterial Material()
    {
        if (_material != null) return _material;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Flora.gdshader") };
        _material.SetShaderParameter("flora_tex", Textures());
        return _material;
    }

    /// <summary>
    /// Upright plants: two crossed 1 m quads standing on the origin. Normals lean up (0.7) so both faces light like a
    /// soft tuft, not like a card (a card's back face went dark against the sun).
    /// </summary>
    public static ArrayMesh Upright()
    {
        if (_upright != null) return _upright;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int q = 0; q < 2; q++)
        {
            float a = q * Mathf.Pi / 2 + .35f;
            var side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .5f;
            var face = new Vector3(-side.Z, 0, side.X).Normalized();
            var n = (face * .3f + Vector3.Up * .7f).Normalized();
            Vector3 p0 = -side, p1 = side, p2 = side + Vector3.Up, p3 = -side + Vector3.Up;
            void V(Vector3 p, float u, float v) { st.SetNormal(n); st.SetUV(new Vector2(u, v)); st.AddVertex(p); }
            V(p0, 0, 1); V(p1, 1, 1); V(p2, 1, 0);
            V(p0, 0, 1); V(p2, 1, 0); V(p3, 0, 0);
        }
        _upright = st.Commit();
        _upright.SurfaceSetMaterial(0, Material());
        return _upright;
    }

    /// <summary>Floating leaves: one flat 1 m quad on the water surface.</summary>
    public static ArrayMesh Flat()
    {
        if (_flat != null) return _flat;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void V(float x, float z, float u, float v) { st.SetNormal(Vector3.Up); st.SetUV(new Vector2(u, v)); st.AddVertex(new Vector3(x, 0, z)); }
        V(-.5f, .5f, 0, 1); V(.5f, .5f, 1, 1); V(.5f, -.5f, 1, 0);
        V(-.5f, .5f, 0, 1); V(.5f, -.5f, 1, 0); V(-.5f, -.5f, 0, 0);
        _flat = st.Commit();
        _flat.SurfaceSetMaterial(0, Material());
        return _flat;
    }

    /// <summary>Free on scene exit (static Godot resources outliving the engine crash on shutdown).</summary>
    public static void Release()
    {
        _upright?.Dispose(); _flat?.Dispose(); _material?.Dispose(); _array?.Dispose();
        _upright = null; _flat = null; _material = null; _array = null;
    }
}
