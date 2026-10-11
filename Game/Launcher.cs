using Godot;
using System.Linq;

namespace Thornwake;

/// <summary>
/// Start screen: choose the rewritten world preview or the original Harthwood valley game. Command-line runs of
/// the original game's smoke test (<c>-- --smoke-test</c>) go straight to the valley so existing tooling still works.
/// </summary>
public partial class Launcher : Control
{
    public override void _Ready()
    {
        Rewrite.DisplaySettings.Apply(GetTree().Root);
        var args = OS.GetCmdlineUserArgs();
        if (args.Any(a => a == "--smoke-test")) { CallDeferred(nameof(Open), "res://Game/Main.tscn"); return; }
        if (args.Any(a => a.StartsWith("--world-smoke=") || a.StartsWith("--bench=") || a == "--world")) { CallDeferred(nameof(Open), "res://Game/Rewrite/WorldLab.tscn"); return; }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color("1f2b2d"), AnchorRight = 1, AnchorBottom = 1 });
        var box = new VBoxContainer { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f,
            OffsetLeft = -230, OffsetRight = 230, OffsetTop = -170, OffsetBottom = 170 };
        box.AddThemeConstantOverride("separation", 14);
        AddChild(box);
        var title = new Label { Text = "THORNWAKE", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 44);
        box.AddChild(title);
        box.AddChild(new Label { Text = "Rewrite preview build", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, .7f) });
        Add(box, "Explore the new world (rewrite preview)", "res://Game/Rewrite/WorldLab.tscn");
        Add(box, "Harthwood Valley (original game)", "res://Game/Main.tscn");
        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(0, 44) };
        quit.Pressed += () => GetTree().Quit();
        box.AddChild(quit);
    }

    private void Add(Container box, string text, string scene)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 52) };
        button.Pressed += () => Open(scene);
        box.AddChild(button);
    }

    private void Open(string scene) => GetTree().ChangeSceneToFile(scene);
}
