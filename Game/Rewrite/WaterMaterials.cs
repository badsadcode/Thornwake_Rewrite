using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// The water's materials, one per kind of water (D284, the water rework's first step): the sea, rivers, and lakes with ponds,
/// each a part of every tile's water mesh. Each can draw with its own shader; until a kind has a look of its own all three
/// draw with today's <see cref="GameSettings.WaterStyle"/>. Every uniform the atmosphere, the wakes and the streamer set goes
/// to all three (the same names as one material had), so a shader written for one material works on each part.
/// </summary>
public sealed class WaterMaterials
{
    /// <summary>The parts of a tile's water mesh, in the order of the kind codes (sea 0, river ⅓, lake ⅔ and pond 1).</summary>
    public const int Sea = 0, River = 1, Lake = 2, Parts = 3;

    private readonly ShaderMaterial[] _all = new ShaderMaterial[Parts];

    public WaterMaterials(Shader shader)
    {
        for (int i = 0; i < Parts; i++) _all[i] = new ShaderMaterial { Shader = shader };
    }

    /// <summary>The material of one part (<see cref="Sea"/>, <see cref="River"/>, <see cref="Lake"/>).</summary>
    public ShaderMaterial this[int part] => _all[part];

    /// <summary>The river's material (the local water simulation's water is drawn as river).</summary>
    public ShaderMaterial RiverMaterial => _all[River];

    /// <summary>One part's shader (each keeps its uniforms when it changes).</summary>
    public void SetShader(int part, Shader shader) { if (_all[part].Shader != shader) _all[part].Shader = shader; }

    /// <summary>A uniform for every part.</summary>
    public void SetShaderParameter(StringName name, Variant value)
    {
        foreach (var m in _all) m.SetShaderParameter(name, value);
    }

    /// <summary>Which part a square of the water mesh belongs to, from its corners' highest kind code (COLOR.g).</summary>
    public static int PartOf(float kindCode) => kindCode < 1f / 6 ? Sea : kindCode < .5f ? River : Lake;
}
