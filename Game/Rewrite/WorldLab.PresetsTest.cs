using System;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// <c>--presets-test</c> (headless, D157): save a preset, change a setting, load the preset back, load the built-in Low,
/// check that world settings are untouched, delete the preset; in a scratch folder, with the player's settings file
/// never written (GameSettings.SaveSuspended) and every value put back. Prints PRESETS_TEST_PASS or …_FAIL, then quits.
/// </summary>
public partial class WorldLab
{
    private bool _presetsTestPending = OS.GetCmdlineUserArgs().Contains("--presets-test");

    private void PresetsTest()
    {
        _presetsTestPending = false;
        var before = GameSettings.All.ToDictionary(s => s, s => s.Value);
        string folder = OS.GetUserDataDir() + "/presets-test-" + DateTime.Now.Ticks;
        GameSettings.SaveSuspended = true;
        SettingPresets.Folder = folder;
        string fail = "";
        void Check(bool ok, string what) { if (!ok && fail.Length == 0) fail = what; }
        try
        {
            var grass = GameSettings.All.First(s => s.Key == "detail.grass_density");
            var season = GameSettings.Season;
            grass.Value = 1.7; season.Value = 2;
            Check(SettingPresets.Save("My test").Contains("saved"), "save");
            Check(SettingPresets.Save("Low").Contains("built-in"), "a built-in name refused");
            grass.Value = .3;
            var mine = SettingPresets.All().FirstOrDefault(p => !p.BuiltIn && p.Name == "My test");
            Check(mine != null, "listed");
            if (mine != null) SettingPresets.Apply(mine);
            Check(Math.Abs(grass.Value - 1.7) < 1e-6, $"loaded back (grass {grass.Value})");
            var low = SettingPresets.All().First(p => p.BuiltIn && p.Name == "Low");
            SettingPresets.Apply(low);
            Check(Math.Abs(grass.Value - .5) < 1e-6, $"Low sets grass 0.5 ({grass.Value})");
            var haze = GameSettings.All.First(s => s.Key == "fx.haze");
            haze.Value = 1.3;
            SettingPresets.Apply(SettingPresets.All().First(p => p.BuiltIn && p.Name == "Ultra"));
            Check(Math.Abs(haze.Value - 1.3) < 1e-6, $"Ultra leaves the look alone (haze {haze.Value})");
            SettingPresets.Apply(low);
            var ssr = GameSettings.All.First(s => s.Key == "fx.ssr");
            Check(ssr.Value == 0, "Low turns reflections off");
            Check(season.Index == 2, "season untouched (a world setting)");
            Check(SettingPresets.All().Where(p => p.BuiltIn).Select(p => p.Name).SequenceEqual(new[] { "Low", "Medium", "High", "Ultra",
                "Look: Clear", "Look: Natural", "Look: Atmospheric", "Look: Dramatic" }), "built-ins Low…Ultra, looks");
            var shadows = GameSettings.All.First(s => s.Key == "fx.shadows");
            double shadowsWas = shadows.Value;
            SettingPresets.Apply(SettingPresets.All().First(p => p.Name == "Look: Dramatic"));
            Check(Math.Abs(GameSettings.All.First(s => s.Key == "fx.ray_strength").Value - 1.8) < 1e-6, "Dramatic sets rays strength");
            Check(shadows.Value == shadowsWas, "a look preset leaves quality alone");
            if (mine != null) Check(SettingPresets.Delete(mine).Contains("deleted"), "delete");
            Check(SettingPresets.All().All(p => p.BuiltIn), "gone after delete");
        }
        catch (Exception e) { fail = "exception: " + e.Message; }
        foreach (var (s, v) in before) s.Value = v;
        GameSettings.SaveSuspended = false;
        SettingPresets.Folder = "user://presets";
        try { System.IO.Directory.Delete(ProjectSettings.GlobalizePath(folder), true); } catch { }
        GD.Print(fail.Length == 0 ? "PRESETS_TEST_PASS" : "PRESETS_TEST_FAIL " + fail);
        GetTree().Quit(fail.Length == 0 ? 0 : 1);
    }
}
