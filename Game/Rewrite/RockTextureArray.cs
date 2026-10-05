using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>The rock looks (<see cref="RockTextures"/>) as one texture array, shared by the terrain and the rocks.</summary>
public static class RockTextureArray
{
    private static Texture2DArray? _array;

    public static Texture2DArray Get()
    {
        if (_array != null) return _array;
        var images = new Godot.Collections.Array<Image>();
        foreach (var file in RockTextures.Files)
        {
            var image = GD.Load<Texture2D>($"res://Assets/Vegetation/Placeholder/{file}.png").GetImage();
            if (image.IsCompressed()) image.Decompress();
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != 128) image.Resize(128, 128, Image.Interpolation.Nearest);
            image.GenerateMipmaps();
            images.Add(image);
        }
        _array = new Texture2DArray();
        _array.CreateFromImages(images);
        return _array;
    }

    /// <summary>Free on scene exit (static Godot resources outliving the engine crash on shutdown).</summary>
    public static void Release() { _array?.Dispose(); _array = null; }
}
