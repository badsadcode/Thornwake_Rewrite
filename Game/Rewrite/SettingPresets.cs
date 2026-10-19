using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Settings presets (D157, Tom: "players can choose from one premade, or make their own, save them, and name them so
/// they can load them later"). A preset covers how the game looks and runs — detail, view distance, lighting and
/// effects, sky looks, camera and performance, render scale — not the world everyone shares (time, weather, simulation:
/// the host's in multiplayer), the interface, the window or audio. Built-in presets are the benchmark's quality profiles
/// (Content/benchmarks.json, one source): every covered setting back to its default, then the profile's values. The
/// player's own presets are files in user://presets (%APPDATA%\Thornwake\presets) with every covered setting.
/// </summary>
public static class SettingPresets
{
    /// <summary>Tests point this elsewhere so they never touch the player's presets.</summary>
    public static string Folder { get; set; } = "user://presets";

    private static readonly string[] Sections = { "Detail", "View distance", "Lighting and effects", "Sky", "Camera and performance", "Display" };

    /// <summary>The settings a preset sets.</summary>
    public static IEnumerable<Setting> Covered => GameSettings.All.Where(s => Sections.Contains(s.Section) && !GameSettings.AffectsWorld(s));

    public sealed record Preset(string Name, bool BuiltIn, string Id);

    /// <summary>Built-in presets first (Low … Ultra), then the player's own by name.</summary>
    public static List<Preset> All()
    {
        var list = new List<Preset>();
        foreach (var (id, node) in Profiles())
        {
            if (id == "current") continue;
            string name = node?["name"]?.GetValue<string>() ?? id;
            list.Add(new Preset(name.Split(" (")[0], true, id)); // ("Medium (game defaults)" → "Medium")
        }
        string dir = ProjectSettings.GlobalizePath(Folder);
        if (Directory.Exists(dir))
            foreach (var file in Directory.GetFiles(dir, "*.cfg").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var cfg = new ConfigFile();
                if (cfg.Load(file) != Error.Ok) continue;
                list.Add(new Preset(cfg.GetValue("preset", "name", Path.GetFileNameWithoutExtension(file)).AsString(), false, file));
            }
        return list;
    }

    private static IEnumerable<(string Id, JsonNode? Node)> Profiles()
    {
        var json = JsonNode.Parse(Godot.FileAccess.GetFileAsString("res://Content/benchmarks.json"));
        if (json?["profiles"] is JsonObject profiles)
            foreach (var (id, node) in profiles) yield return (id, node);
    }

    /// <summary>Set every covered setting from the preset. Returns a line for the status bar.</summary>
    public static string Apply(Preset preset)
    {
        if (preset.BuiltIn)
        {
            var profile = Profiles().FirstOrDefault(p => p.Id == preset.Id).Node?["settings"] as JsonObject;
            var values = profile?.ToDictionary(p => p.Key, p => p.Value!.GetValue<double>()) ?? new();
            foreach (var s in Covered) s.Value = values.TryGetValue(s.Key, out double v) ? v : s.Default;
            return $"Preset {preset.Name} loaded.";
        }
        var cfg = new ConfigFile();
        if (cfg.Load(preset.Id) != Error.Ok) return $"Could not read the preset {preset.Name}.";
        int set = 0;
        foreach (var s in Covered)
        {
            var v = cfg.GetValue("settings", s.Key, Variant.From(double.NaN));
            if (v.VariantType is Variant.Type.Float or Variant.Type.Int && !double.IsNaN(v.AsDouble())) { s.Value = v.AsDouble(); set++; }
            // (a setting newer than the preset keeps its value)
        }
        return $"Preset {preset.Name} loaded ({set} settings).";
    }

    /// <summary>Save the covered settings as the player's preset (replacing one of the same name).</summary>
    public static string Save(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "Give the preset a name first.";
        if (All().Any(p => p.BuiltIn && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"{name} is a built-in preset: choose another name.";
        string dir = ProjectSettings.GlobalizePath(Folder);
        Directory.CreateDirectory(dir);
        var cfg = new ConfigFile();
        cfg.SetValue("preset", "name", name);
        cfg.SetValue("preset", "saved", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
        cfg.SetValue("preset", "version", ProjectSettings.GetSetting("application/config/version", "?").AsString());
        foreach (var s in Covered) cfg.SetValue("settings", s.Key, s.Value);
        return cfg.Save(Path.Combine(dir, FileName(name))) == Error.Ok ? $"Preset {name} saved." : $"Could not save the preset {name}.";
    }

    public static string Delete(Preset preset)
    {
        if (preset.BuiltIn) return "Built-in presets cannot be deleted.";
        try { File.Delete(preset.Id); return $"Preset {preset.Name} deleted."; }
        catch (Exception e) { return $"Could not delete {preset.Name}: {e.Message}"; }
    }

    /// <summary>A file name from the preset's name (letters, digits, spaces, dashes; the name itself is kept inside).</summary>
    private static string FileName(string name)
    {
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_').ToArray()).Trim();
        return (safe.Length == 0 ? "preset" : safe.Length > 60 ? safe[..60] : safe) + ".cfg";
    }
}
