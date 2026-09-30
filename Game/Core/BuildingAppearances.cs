using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Thornwake.Core;

// Cosmetic choices never participate in costs, support, snapping or collision.
public sealed class BuildingAppearanceDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "a18b61";
    public string Pattern { get; set; } = "plain";
    public List<BuildingModelDefinition> Models { get; set; } = new();
}
public sealed class BuildingModelDefinition
{
    public string Variant { get; set; } = "";
    public float Slope { get; set; }
    public bool Mirrored { get; set; }
    public string Scene { get; set; } = "";
}
public static class BuildingAppearances
{
    public static BuildingAppearanceDefinition? Find(ContentCatalog catalog, BuildSelection selection)
        => catalog.BuildingTools[selection.DefinitionId].Appearances.FirstOrDefault(a => a.Id == selection.AppearanceId);

    public static BuildingModelDefinition? Model(ContentCatalog catalog, BuildSelection selection)
        => Find(catalog, selection)?.Models.FirstOrDefault(m => m.Variant == selection.VariantId &&
            MathF.Abs(m.Slope - selection.Slope) < .001f && m.Mirrored == selection.Mirrored);

    public static BuildSelection? SelectionFor(ContentCatalog catalog, StructureState piece)
    {
        if(piece.Selection!=null)return piece.Selection.Copy();
        foreach(var tool in catalog.BuildingTools.Values.Where(t=>t.Appearances.Count>0))
        {
            var variant=tool.Variants.FirstOrDefault(v=>v.Kind==piece.Kind&&v.Geometry=="legacy");
            if(variant==null||!tool.AllowedRotations.Any(a=>MathF.Abs(MathF.IEEERemainder(piece.Rotation-a*MathF.PI/180,MathF.Tau))<.001f))continue;
            var selected=BuildingVariants.Default(tool);selected.VariantId=variant.Id;selected.Slope=0;
            return selected;
        }
        return null;
    }
    public static void Validate(BuildingToolDefinition tool)
    {
        if (tool.Appearances == null || tool.Appearances.Any(a => a == null) ||
            tool.Appearances.Select(a => a.Id).Distinct().Count() != tool.Appearances.Count)
            throw new InvalidDataException("Invalid appearance collection.");
        foreach (var appearance in tool.Appearances)
        {
            if (string.IsNullOrWhiteSpace(appearance.Id) || appearance.Id.Length > 100 || string.IsNullOrWhiteSpace(appearance.Name) ||
                appearance.ColorHex == null || !Regex.IsMatch(appearance.ColorHex, "^#?[0-9a-fA-F]{6}$") ||
                appearance.Pattern is not ("plain" or "shingle" or "tile" or "thatch" or "plank" or "masonry") ||
                appearance.Models == null || appearance.Models.Any(m => m == null))
                throw new InvalidDataException("Invalid building appearance.");
            if (appearance.Models.Select(m => (m.Variant,m.Slope,m.Mirrored)).Distinct().Count() != appearance.Models.Count)
                throw new InvalidDataException("Ambiguous building model mapping.");
            foreach (var model in appearance.Models)
            {
                var variant = tool.Variants.FirstOrDefault(v => v.Id == model.Variant);
                if (variant == null || !float.IsFinite(model.Slope) || !variant.Slopes.Contains(model.Slope) ||
                    model.Mirrored && !variant.SupportsMirroring || model.Scene == null ||
                    !Regex.IsMatch(model.Scene, @"^res://Assets/Buildings/[a-zA-Z0-9_/-]+\.(glb|tscn)$"))
                    throw new InvalidDataException("Invalid building model mapping. Use an exact variant/pitch and a scene under Assets/Buildings.");
            }
        }
    }
}
public sealed partial class Simulation
{
    public ActionResult Refinish(string id, string appearanceId)
    {
        if (State.Player.EquippedItem != "hammer" || Count("hammer") < 1) return Fail("Equip your hammer to change a finish.");
        if (State.CraftJob != null) return Fail("Finish or cancel your current crafting task first.");
        var piece = State.Structures.FirstOrDefault(s => s.Id == id);
        if (piece == null) return Fail("That piece has already been removed.");
        var selected=BuildingAppearances.SelectionFor(Catalog,piece);
        if(selected==null)return Fail("This piece does not support cosmetic finishes yet.");
        if (Construction.Distance(State.Player.X,State.Player.Z,piece.X,piece.Z) > Construction.Reach ||
            MathF.Abs(Construction.Base(State,piece) - (State.Player.Y ?? Construction.Ground(State,State.Player.X,State.Player.Z))) > Construction.Storey * 3)
            return Fail("Move closer to change this finish.");
        var tool = Catalog.BuildingTools[selected.DefinitionId];
        if (appearanceId == null || appearanceId != "" && !tool.Appearances.Any(a => a.Id == appearanceId))
            return Fail("That finish is not available for this piece.");
        if (selected.AppearanceId == appearanceId) return Fail("This piece already has that finish.");
        piece.Selection = selected;
        piece.Selection.AppearanceId = appearanceId;
        return Done("Finish changed to " + (appearanceId == "" ? "original material" : tool.Appearances.First(a => a.Id == appearanceId).Name) + ".");
    }
}
