using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>Menu → Display: window mode, monitor, window size (with aspect ratio), UI scale, then V-Sync, frame limit and
/// render scale (DisplaySettings). A changed mode, monitor or size asks to be kept and reverts by itself after 15 s, in
/// case the new mode shows nothing.</summary>
public partial class WorldLab
{
    private OptionButton _displayMode = null!, _displayScreen = null!, _displaySize = null!, _displayUi = null!;
    private Label _displayInfo = null!, _displayConfirmText = null!;
    private HBoxContainer _displayConfirm = null!;
    private Timer _displayTimer = null!;
    private (DisplaySettings.Mode Mode, Vector2I Size, int Screen, float Ui)? _displayPrevious;
    private int _displayCountdown;
    private List<Vector2I> _displaySizes = new();

    private Control BuildDisplayTab()
    {
        var box = new VBoxContainer { Name = "Display" };
        box.AddThemeConstantOverride("separation", 8);
        _displayInfo = new Label { Modulate = new Color(1, 1, 1, .75f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_displayInfo);

        OptionButton Row(string label)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(220, 0) });
            var pick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddChild(pick);
            box.AddChild(row);
            return pick;
        }
        _displayMode = Row("Window mode");
        foreach (var n in DisplaySettings.ModeNames) _displayMode.AddItem(n);
        _displayScreen = Row("Monitor");
        _displaySize = Row("Window size");
        _displayUi = Row("UI scale");
        foreach (var u in DisplaySettings.UiScales) _displayUi.AddItem($"{u * 100:0} %");
        box.AddChild(new Label
        {
            Text = "Fullscreen modes always use the monitor's own resolution (sharpest text and UI). For more frames per second " +
                   "lower Render scale below: the 3D view is drawn smaller and upscaled (FSR), the UI stays sharp. Ultrawide " +
                   "screens (21:9, 32:9) get a wider view with the HUD in the corners.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, .7f),
        });

        _displayConfirm = new HBoxContainer { Visible = false };
        _displayConfirmText = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var keep = new Button { Text = "Keep" };
        keep.Pressed += DisplayKeep;
        var revert = new Button { Text = "Revert" };
        revert.Pressed += DisplayRevert;
        _displayConfirm.AddChild(_displayConfirmText); _displayConfirm.AddChild(keep); _displayConfirm.AddChild(revert);
        box.AddChild(_displayConfirm);
        _displayTimer = new Timer { WaitTime = 1 };
        _displayTimer.Timeout += () =>
        {
            _displayCountdown--;
            _displayConfirmText.Text = $"Keep these display settings? Reverting in {_displayCountdown} s.";
            if (_displayCountdown <= 0) DisplayRevert();
        };
        box.AddChild(_displayTimer);

        box.AddChild(SettingsUi.List(GameSettings.All.Where(s => s.Section == "Display"), GameSettings.ResetSection));

        _displayMode.ItemSelected += i => DisplayChange(() => DisplaySettings.WindowMode = (DisplaySettings.Mode)(int)i);
        _displayScreen.ItemSelected += i => DisplayChange(() => DisplaySettings.Screen = (int)i);
        _displaySize.ItemSelected += i => DisplayChange(() => DisplaySettings.Size = _displaySizes[(int)i]);
        _displayUi.ItemSelected += i => { DisplaySettings.UiScale = DisplaySettings.UiScales[(int)i]; DisplaySettings.Apply(GetTree().Root); DisplaySettings.Save(); DisplayRefresh(); };
        // Leaving the page (closing the menu, another tab) keeps a pending change: the player could see to do it, so the new
        // mode works — the countdown used to run on hidden and put the old mode back 15 s later (Tom: "it goes back to
        // full screen after some time").
        box.VisibilityChanged += () => { if (box.IsVisibleInTree()) DisplayRefresh(); else if (_displayPrevious != null) DisplayKeep(); };
        DisplayRefresh();
        return box;
    }

    /// <summary>Fill the choices from the current monitors and settings.</summary>
    private void DisplayRefresh()
    {
        _displayMode.Select((int)DisplaySettings.WindowMode);
        _displayScreen.Clear();
        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var size = DisplayServer.ScreenGetSize(i);
            _displayScreen.AddItem($"Monitor {i + 1}: {size.X} × {size.Y} ({DisplaySettings.Aspect(size)}), {DisplaySettings.Hz(i)}" +
                                   (i == DisplayServer.GetPrimaryScreen() ? ", main" : ""));
        }
        int screen = DisplaySettings.ScreenIndex;
        _displayScreen.Select(screen);
        _displaySize.Clear();
        _displaySizes = DisplaySettings.Resolutions(screen);
        var native = DisplayServer.ScreenGetSize(screen);
        foreach (var r in _displaySizes) _displaySize.AddItem(DisplaySettings.Describe(r) + (r == native ? " — monitor size" : ""));
        bool windowed = DisplaySettings.WindowMode == DisplaySettings.Mode.Windowed;
        var shown = windowed ? DisplaySettings.Size : native;
        int index = _displaySizes.FindIndex(r => r == shown);
        if (index < 0) { _displaySizes.Add(shown); _displaySize.AddItem(DisplaySettings.Describe(shown) + " (custom)"); index = _displaySizes.Count - 1; }
        _displaySize.Select(index);
        _displaySize.Disabled = !windowed;
        _displaySize.TooltipText = windowed ? "" : "Fullscreen uses the monitor's resolution; lower Render scale for speed.";
        _displayUi.Select(Math.Max(0, Array.FindIndex(DisplaySettings.UiScales, u => Math.Abs(u - DisplaySettings.UiScale) < .01f)));
        _displayInfo.Text = "Now: " + DisplaySettings.Current();
    }

    private void DisplayChange(Action change)
    {
        _displayPrevious ??= (DisplaySettings.WindowMode, DisplaySettings.Size, DisplaySettings.Screen, DisplaySettings.UiScale);
        change();
        DisplaySettings.Apply(GetTree().Root);
        _displayCountdown = 15;
        _displayConfirmText.Text = $"Keep these display settings? Reverting in {_displayCountdown} s.";
        _displayConfirm.Visible = true;
        _displayTimer.Start();
        CallDeferred(nameof(DisplayRefresh));
    }

    private void DisplayKeep()
    {
        _displayPrevious = null;
        _displayTimer.Stop();
        _displayConfirm.Visible = false;
        DisplaySettings.Save();
    }

    private void DisplayRevert()
    {
        _displayTimer.Stop();
        _displayConfirm.Visible = false;
        if (_displayPrevious is { } p)
        {
            DisplaySettings.WindowMode = p.Mode; DisplaySettings.Size = p.Size; DisplaySettings.Screen = p.Screen; DisplaySettings.UiScale = p.Ui;
            DisplaySettings.Apply(GetTree().Root);
            DisplaySettings.Save();
        }
        _displayPrevious = null;
        CallDeferred(nameof(DisplayRefresh));
    }
}
