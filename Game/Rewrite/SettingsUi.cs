using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>Builds menu controls for <see cref="Setting"/>s: one row per setting (label with tooltip, control, value).</summary>
public static class SettingsUi
{
    /// <summary>A scrollable list of settings grouped by section, with a reset button per section.</summary>
    public static Control List(IEnumerable<Setting> settings, System.Action<string>? reset = null)
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(box);
        foreach (var section in settings.GroupBy(s => s.Section))
        {
            var header = new HBoxContainer();
            var title = new Label { Text = section.Key, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            title.AddThemeColorOverride("font_color", new Color("e8d9a8"));
            title.AddThemeFontSizeOverride("font_size", 17);
            header.AddChild(title);
            if (reset != null)
            {
                var button = new Button { Text = "Reset", Flat = true };
                string name = section.Key;
                button.Pressed += () => reset(name);
                header.AddChild(button);
            }
            box.AddChild(header);
            foreach (var s in section) box.AddChild(Row(s));
            box.AddChild(new HSeparator());
        }
        return scroll;
    }

    public static Control Row(Setting s)
    {
        var row = new HBoxContainer { TooltipText = s.Tip };
        var label = new Label { Text = s.Label, CustomMinimumSize = new Vector2(230, 0), TooltipText = s.Tip, MouseFilter = Control.MouseFilterEnum.Pass,
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        row.AddChild(label);
        if (s.Options != null)
        {
            var options = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = s.Tip };
            foreach (var o in s.Options) options.AddItem(o);
            options.Select(s.Index);
            options.ItemSelected += i => s.Value = i;
            s.Changed += x => { if (GodotObject.IsInstanceValid(options)) options.Select(x.Index); };
            row.AddChild(options);
            return row;
        }
        if (s.IsToggle)
        {
            var check = new CheckButton { ButtonPressed = s.On, TooltipText = s.Tip };
            check.Toggled += on => s.Value = on ? 1 : 0;
            s.Changed += x => { if (GodotObject.IsInstanceValid(check)) check.SetPressedNoSignal(x.On); };
            row.AddChild(check);
            return row;
        }
        var slider = new HSlider
        {
            MinValue = s.Min, MaxValue = s.Max, Step = s.Step, Value = s.Value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = s.Tip,
        };
        var value = new Label { Text = s.Display(), CustomMinimumSize = new Vector2(78, 0), HorizontalAlignment = HorizontalAlignment.Right };
        // Rebuilding settings apply when the slider is released (dragging would restart tile builds every frame).
        if (s.Rebuild)
        {
            slider.ValueChanged += v => value.Text = Preview(s, v);
            slider.DragEnded += _ => s.Value = slider.Value;
            slider.GuiInput += e => { if (e is InputEventMouseButton { Pressed: false } or InputEventKey) s.Value = slider.Value; };
        }
        else slider.ValueChanged += v => s.Value = v;
        s.Changed += x =>
        {
            if (!GodotObject.IsInstanceValid(slider)) return;
            slider.SetValueNoSignal(x.Value); value.Text = x.Display();
        };
        row.AddChild(slider);
        row.AddChild(value);
        return row;
    }

    private static string Preview(Setting s, double v)
    {
        double old = s.Value;
        s.LoadValue(v);
        string text = s.Display();
        s.LoadValue(old);
        return text;
    }
}
