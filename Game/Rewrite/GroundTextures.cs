using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Noor's seamless pixel-art ground textures (Assets/Terrain/Noor, 512 px, art pixels ~4–5 px) as one texture array
/// for the terrain's optional "Noor" ground style (setting fx.ground_textures). Loaded at 256 px (art pixels stay
/// crisp, ~8 m per tile in the shader) with mipmaps, plus each layer's mean colour (sRGB) so the shader can use a
/// texture as detail over the biome colours without changing their average.
/// </summary>
public static class GroundTextures
{
    /// <summary>Layer order (the shader's indices).</summary>
    public static readonly string[] Files =
    {
        "grass", "dirt", "sand", "rock", "river_bed", "sea_bed", "forest_floor", "gravel",
        "mud", "wet_mud", "moss", "scree", "snow", "swamp_peat", "ice", "volcanic_rock",
    };

    private static Texture2DArray? _array;
    private static Vector3[]? _means;

    public static Texture2DArray Get()
    {
        if (_array != null) return _array;
        var images = new Godot.Collections.Array<Image>();
        _means = new Vector3[Files.Length];
        for (int i = 0; i < Files.Length; i++)
        {
            var image = GD.Load<Texture2D>($"res://Assets/Terrain/Noor/{Files[i]}.png").GetImage();
            if (image.IsCompressed()) image.Decompress();
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != 256) image.Resize(256, 256, Image.Interpolation.Lanczos);
            double r = 0, g = 0, b = 0;
            for (int y = 0; y < 256; y += 2)
                for (int x = 0; x < 256; x += 2) { var c = image.GetPixel(x, y); r += c.R; g += c.G; b += c.B; }
            double n = 128 * 128;
            _means[i] = new Vector3((float)(r / n), (float)(g / n), (float)(b / n));
            image.GenerateMipmaps();
            images.Add(image);
        }
        _array = new Texture2DArray();
        _array.CreateFromImages(images);
        return _array;
    }

    /// <summary>Mean colour (sRGB, 0–1) of each layer.</summary>
    public static Vector3[] Means { get { Get(); return _means!; } }

    /// <summary>Free on scene exit (static Godot resources outliving the engine crash on shutdown).</summary>
    public static void Release() { _array?.Dispose(); _array = null; _means = null; }
}
