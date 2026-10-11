using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Window mode, monitor, window size and UI scale (Tom, 2026-10-11: "I cannot run the game at my native 1920×1080"). Saved in
/// user://display.cfg and applied on start by the launcher and the world.
/// * Borderless fullscreen and exclusive fullscreen always use the monitor's own resolution: Godot never changes the
///   monitor's video mode. A lower 3D resolution is *Render scale* (with FSR upscaling) instead, which keeps the UI sharp.
/// * Godot cannot list a monitor's video modes, so windowed sizes are the common sizes of every aspect ratio (4:3, 16:10,
///   16:9, 21:9, 32:9) that fit the monitor, plus the monitor's own size.
/// * The UI is laid out on a 1440×900 canvas stretched to the window ("expand": wider or taller windows get more room
///   rather than black bars, so 21:9 and 32:9 screens keep the HUD in the corners and menus centred); UI scale multiplies it.
/// Command line (benchmarks, tests): <c>--window=1920x1080</c>, <c>--window-mode=windowed|borderless|fullscreen</c>; runs that
/// render screenshots (--world-shot, --world-smoke) keep the window they were started with.
/// </summary>
public static class DisplaySettings
{
    public enum Mode { Windowed, Borderless, Fullscreen }

    public static Mode WindowMode { get; set; } = Mode.Borderless;
    public static Vector2I Size { get; set; } = new(1600, 900);
    /// <summary>Monitor index; -1 = the one the window is on.</summary>
    public static int Screen { get; set; } = -1;
    public static float UiScale { get; set; } = 1;

    public static readonly string[] ModeNames = { "Windowed", "Borderless fullscreen", "Fullscreen (exclusive)" };
    public static readonly float[] UiScales = { .75f, .9f, 1, 1.1f, 1.25f, 1.5f, 1.75f, 2 };

    private const string FilePath = "user://display.cfg";
    private static bool _loaded, _automation;

    private static readonly Vector2I[] Common =
    {
        new(1024, 768), new(1280, 960), new(1600, 1200),                                   // 4:3
        new(1280, 800), new(1440, 900), new(1680, 1050), new(1920, 1200), new(2560, 1600), new(2880, 1800), new(3840, 2400), // 16:10
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3200, 1800), new(3840, 2160), // 16:9
        new(2560, 1080), new(3440, 1440), new(3840, 1600), new(5120, 2160),                // 21:9
        new(3840, 1080), new(5120, 1440), new(7680, 2160),                                 // 32:9
    };

    public static int ScreenIndex => Screen >= 0 && Screen < DisplayServer.GetScreenCount() ? Screen : DisplayServer.WindowGetCurrentScreen();

    /// <summary>Window sizes offered on a monitor: common sizes that fit, and the monitor's own size, smallest first.</summary>
    public static List<Vector2I> Resolutions(int screen)
    {
        var native = DisplayServer.ScreenGetSize(screen);
        return Common.Where(r => r.X <= native.X && r.Y <= native.Y).Append(native).Distinct().OrderBy(r => r.X * r.Y).ThenBy(r => r.X).ToList();
    }

    public static string Aspect(Vector2I r)
    {
        double a = r.X / (double)Math.Max(1, r.Y);
        return a switch
        {
            < 1.4 => "4:3", < 1.7 => "16:10", < 1.9 => "16:9", < 2.6 => "21:9", < 3.2 => "24:10", < 4 => "32:9", _ => $"{a:0.00}:1",
        };
    }

    public static string Describe(Vector2I r) => $"{r.X} × {r.Y} ({Aspect(r)})";

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var args = OS.GetCmdlineUserArgs();
        _automation = args.Any(a => a.StartsWith("--world-shot=") || a.StartsWith("--world-smoke=") || a == "--smoke-test");
        var file = new ConfigFile();
        if (file.Load(FilePath) == Error.Ok)
        {
            WindowMode = (Mode)Math.Clamp(file.GetValue("display", "mode", (int)WindowMode).AsInt32(), 0, 2);
            Size = new Vector2I(file.GetValue("display", "width", Size.X).AsInt32(), file.GetValue("display", "height", Size.Y).AsInt32());
            Screen = file.GetValue("display", "screen", Screen).AsInt32();
            UiScale = Math.Clamp(file.GetValue("display", "ui_scale", UiScale).AsSingle(), .5f, 3);
        }
        else
        {
            // First start: borderless fullscreen at the monitor's own resolution; the windowed size it would switch to
            // is the largest common size that leaves room for the taskbar.
            var native = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
            Size = Resolutions(DisplayServer.WindowGetCurrentScreen()).LastOrDefault(r => r.X <= native.X * .9 && r.Y <= native.Y * .9, new Vector2I(1280, 720));
            WindowMode = Mode.Borderless;
        }
        foreach (string a in args)
        {
            if (a.StartsWith("--window=") && a[9..].Split('x') is { Length: 2 } wh
                && int.TryParse(wh[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && int.TryParse(wh[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
            { Size = new Vector2I(w, h); WindowMode = Mode.Windowed; _automation = false; }
            if (a.StartsWith("--window-mode="))
            {
                WindowMode = a[14..] switch { "windowed" => Mode.Windowed, "fullscreen" => Mode.Fullscreen, _ => Mode.Borderless };
                _automation = false;
            }
        }
    }

    public static void Save()
    {
        var file = new ConfigFile();
        file.SetValue("display", "mode", (int)WindowMode);
        file.SetValue("display", "width", Size.X);
        file.SetValue("display", "height", Size.Y);
        file.SetValue("display", "screen", Screen);
        file.SetValue("display", "ui_scale", UiScale);
        file.Save(FilePath);
    }

    /// <summary>Apply the saved (or just chosen) display settings to the main window.</summary>
    public static void Apply(Window root)
    {
        Load();
        root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        root.ContentScaleFactor = UiScale;
        if (_automation || DisplayServer.GetName() == "headless") return;
        int screen = ScreenIndex;
        if (DisplayServer.WindowGetCurrentScreen() != screen)
        {
            if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetCurrentScreen(screen);
        }
        switch (WindowMode)
        {
            case Mode.Windowed:
            {
                if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                var usable = DisplayServer.ScreenGetUsableRect(screen);
                var size = new Vector2I(Math.Clamp(Size.X, 640, Math.Max(640, usable.Size.X)), Math.Clamp(Size.Y, 360, Math.Max(360, usable.Size.Y)));
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
                break;
            }
            case Mode.Borderless: DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); break;
            case Mode.Fullscreen: DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen); break;
        }
    }

    /// <summary>The player resized or moved the window themselves: remember it (windowed only).</summary>
    public static void WindowChanged()
    {
        if (_automation || WindowMode != Mode.Windowed || DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
        Size = DisplayServer.WindowGetSize();
        Screen = DisplayServer.WindowGetCurrentScreen();
        Save();
    }

    /// <summary>"60 Hz", or "? Hz" where the system does not report it.</summary>
    public static string Hz(int screen)
    {
        double hz = DisplayServer.ScreenGetRefreshRate(screen);
        return double.IsFinite(hz) && hz > 0 ? $"{hz:0} Hz" : "? Hz";
    }

    public static string Current()
    {
        var s = DisplayServer.WindowGetSize();
        return $"{ModeNames[(int)WindowMode]} · window {s.X} × {s.Y} ({Aspect(s)}) on monitor {ScreenIndex + 1} of {DisplayServer.GetScreenCount()} " +
               $"({DisplayServer.ScreenGetSize(ScreenIndex).X} × {DisplayServer.ScreenGetSize(ScreenIndex).Y}, {DisplaySettings.Hz(ScreenIndex)}) · UI {UiScale * 100:0} %";
    }
}
