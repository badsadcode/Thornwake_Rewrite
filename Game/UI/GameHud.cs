using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Thornwake.UI;

/// <summary>Presentation only: every player action is returned to the game as a command.</summary>
public partial class GameHud : CanvasLayer
{
    public event Action<string>? Command;
    public event Action<int>? StartRequested;

    private static readonly Color Ink = new("171c1b");
    private static readonly Color Cream = new("e9e4d2");
    private static readonly Color Muted = new("a6b0a2");
    private static readonly Color Gold = new("c5ad73");
    private Control _root = null!, _hud = null!, _title = null!, _panel = null!, _pause = null!;
    private Label _time = null!, _resources = null!, _rumor = null!, _context = null!, _notice = null!;
    private ProgressBar _health = null!, _stamina = null!;
    private Label _healthNumber = null!, _staminaNumber = null!;
    private LineEdit _seed = null!;
    private Button _continue = null!, _soundButton = null!;
    private Button _cameraMode = null!, _invertY = null!;
    private HSlider _cameraSensitivity = null!;
    private Label _cameraSensitivityLabel = null!, _cameraHint = null!, _reticle = null!;
    private Label _buildHeightHint = null!;
    private bool _alwaysCamera, _invertedCamera, _updatingCameraOptions;
    private VBoxContainer _panelBody = null!;
    private Label _panelTitle = null!;
    private float _noticeTime;
    private bool _sound = true;
    private Font _serif = null!;
    public bool IsPanelOpen => (_panel?.Visible ?? false) || (_pause?.Visible ?? false) || (_collection?.Visible ?? false) || IsPlayerScreenOpen || IsSandboxOpen || IsWorldSetupOpen;
    public bool IsTitleVisible => _title?.Visible ?? false;

    public override void _Ready()
    {
        Layer = 10;
        _serif = new SystemFont { FontNames = new[] { "Georgia", "Times New Roman", "DejaVu Serif" } };
        _root = Full(new Control { Name = "Interface", MouseFilter = Control.MouseFilterEnum.Ignore });
        AddChild(_root);
        var theme = new Theme { DefaultFontSize = 16 };
        theme.SetColor("font_color", "Label", Cream);
        theme.SetColor("font_color", "Button", Cream);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", new Color("647069"));
        theme.SetStylebox("normal", "Button", Box(new Color("25302a"), new Color("52604b")));
        theme.SetStylebox("hover", "Button", Box(new Color("364536"), Gold));
        theme.SetStylebox("pressed", "Button", Box(new Color("48543b"), Gold));
        theme.SetStylebox("focus", "Button", Box(Colors.Transparent, Gold));
        theme.SetStylebox("disabled", "Button", Box(new Color("202823"), new Color("354039")));
        _root.Theme = theme;
        BuildHud();
        BuildCraftActivity();
        BuildPlacementOptions();
        BuildEarthworkTools();
        BuildTitle();
        BuildPanel();
        BuildCollection();
        BuildPlayerScreens();
        BuildPause();
        BuildSandbox();
        BuildWorldSetup();
        _notice = Text("", 18, Cream);
        _notice.HorizontalAlignment = HorizontalAlignment.Center;
        Place(_notice, .5f, .12f, -400, 0, 800, 44);
        _notice.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .95f));
        _notice.AddThemeConstantOverride("shadow_offset_x", 1);
        _notice.AddThemeConstantOverride("shadow_offset_y", 2);
        _root.AddChild(_notice);
        ShowTitle(false);
    }

    private void BuildHud()
    {
        _hud = Full(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        _root.AddChild(_hud);
        var identity = Card(284, 142);
        Place(identity, 0, 0, 24, 24, 284, 142);
        _hud.AddChild(identity);
        var stack = Stack(identity, 18, 13, 7);
        var brand = Text("THORNWAKE", 21, Cream, true);
        stack.AddChild(brand);
        stack.AddChild(Text("HARTHWOOD VALLEY", 10, Gold));
        AddVital(stack, "Health", new Color("a56957"), out _health, out _healthNumber);
        AddVital(stack, "Stamina", new Color("a6b37d"), out _stamina, out _staminaNumber);

        var world = Card(286, 114);
        Place(world, 1, 0, -310, 24, 286, 114);
        _hud.AddChild(world);
        var ws = Stack(world, 17, 13, 9);
        _time = Text("SPRING · DAY 1   /   08:00", 14, Cream);
        ws.AddChild(_time);
        var line = new HSeparator();
        line.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color("52604b"), Thickness = 1 });
        ws.AddChild(line);
        _resources = Text("Wood 0   Stone 0   Coin 0", 14, Muted);
        _resources.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ws.AddChild(_resources);

        var journal = Card(266, 154);
        Place(journal, 1, 0, -290, 158, 266, 154);
        _hud.AddChild(journal);
        var js = Stack(journal, 17, 14, 10);
        js.AddChild(Text("A PLACE TO BEGIN", 11, Gold));
        _rumor = Text("Gather fallen branches. Speak to the people of Harthwood. Find your place in the valley.", 14, Cream);
        _rumor.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        js.AddChild(_rumor);

        BuildHudHotbar();
        _context = Text("", 17, Cream);
        _context.HorizontalAlignment = HorizontalAlignment.Center;
        _context.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .9f));
        _context.AddThemeConstantOverride("shadow_offset_x", 1);
        _context.AddThemeConstantOverride("shadow_offset_y", 2);
        Place(_context, .5f, 1, -420, -186, 840, 36);
        _hud.AddChild(_context);
        var controls = Text("WASD  Move     SHIFT  Sprint     1–9 / 0  Quick access     E  Interact     I  Satchel     C  Craft     B  Build (hammer)     M  Map     ESC  Menu", 11, Cream);
        controls.HorizontalAlignment = HorizontalAlignment.Center;
        controls.AddThemeColorOverride("font_shadow_color", Colors.Black);
        controls.AddThemeConstantOverride("shadow_offset_y", 2);
        Place(controls, .5f, 1, -600, -32, 1200, 24);
        _hud.AddChild(controls);
        _cameraHint=Text("Hold middle mouse or both mouse buttons to orbit  ·  Hold Q / R to turn",11,Muted);
        _cameraHint.HorizontalAlignment=HorizontalAlignment.Center;
        _cameraHint.AddThemeColorOverride("font_shadow_color",Colors.Black);
        _cameraHint.AddThemeConstantOverride("shadow_offset_y",1);
        Place(_cameraHint,.5f,1,-440,-211,880,22);_hud.AddChild(_cameraHint);
        _buildHeightHint=Text("",14,Gold);_buildHeightHint.HorizontalAlignment=HorizontalAlignment.Center;
        _buildHeightHint.AddThemeColorOverride("font_shadow_color",Colors.Black);_buildHeightHint.AddThemeConstantOverride("shadow_offset_y",1);
        Place(_buildHeightHint,.5f,1,-500,-243,1000,26);_hud.AddChild(_buildHeightHint);
        _reticle=Text("+",22,Cream);
        _reticle.HorizontalAlignment=HorizontalAlignment.Center;
        _reticle.AddThemeColorOverride("font_shadow_color",Colors.Black);
        _reticle.AddThemeConstantOverride("shadow_offset_y",1);
        Place(_reticle,.5f,.43f,-12,-16,24,32);_hud.AddChild(_reticle);_reticle.Hide();
    }

    private void BuildTitle()
    {
        _title = Modal(new Color(.045f, .065f, .052f, .8f));
        var card = Card(550, 650);
        Place(card, .5f, .5f, -275, -335, 550, 650);
        _title.AddChild(card);
        var s = Stack(card, 42, 33, 12);
        s.AddChild(Text("AN OPEN WORLD. A LIFE OF YOUR OWN.", 11, Gold));
        s.AddChild(Text("THORNWAKE", 49, Cream, true));
        s.AddChild(Text("A life at the edge of the wild", 21, Muted, true));
        Space(s, 10);
        var intro = Text("Arrive in a living valley. Gather, craft and grow.\nHelp a settlement find its footing, and make\na home beneath the old trees.", 16, Cream);
        intro.AddThemeConstantOverride("line_spacing", 6);
        s.AddChild(intro);
        Space(s, 3);
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 18);
        seedRow.AddChild(Text("WORLD SEED", 11, Gold));
        _seed = new LineEdit { Text = "7319", PlaceholderText = "7319", CustomMinimumSize = new Vector2(125, 34), MaxLength = 9 };
        _seed.AddThemeStyleboxOverride("normal", Box(new Color("151e19"), new Color("4b594a")));
        seedRow.AddChild(_seed);
        s.AddChild(seedRow);
        var start = ActionButton("Enter the valley", () => { if (!int.TryParse(_seed.Text, out int seed)) seed = 7319; StartRequested?.Invoke(seed); }, true);
        s.AddChild(start);
        var customize=ActionButton("Customize new world",ShowWorldSetup);customize.Name="CustomizeNewWorld";s.AddChild(customize);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _continue = ActionButton("Continue", () => Command?.Invoke("continue"));
        _continue.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(_continue);
        var quit = ActionButton("Quit", () => Command?.Invoke("quit"));
        quit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(quit);
        s.AddChild(row);
        var footer = Text("FIRST PLAYABLE   /   0.8.0", 11, Muted);
        footer.HorizontalAlignment = HorizontalAlignment.Center;
        Place(footer, .5f, 1, -250, -47, 500, 24);
        _title.AddChild(footer);
    }

    private void BuildPanel()
    {
        _panel = Modal(new Color(0, 0, 0, .42f));
        var card = Card(640, 550);
        Place(card, .5f, .5f, -320, -275, 640, 550);
        _panel.AddChild(card);
        var s = Stack(card, 29, 25, 18);
        var header = new HBoxContainer();
        _panelTitle = Text("Satchel", 30, Cream, true);
        _panelTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_panelTitle);
        header.AddChild(ActionButton("Close  [Esc]", HidePanel));
        s.AddChild(header);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _panelBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _panelBody.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_panelBody);
        s.AddChild(scroll);
        _panel.Hide();
    }

    private void BuildPause()
    {
        _pause = Modal(new Color(0, 0, 0, .55f));
        var card = Card(480, 730);
        Place(card, .5f, .5f, -240, -365, 480, 730);
        _pause.AddChild(card);
        var s = Stack(card, 31, 28, 13);
        s.AddChild(Text("A moment of rest", 28, Cream, true));
        s.AddChild(Text("HARTHWOOD VALLEY", 11, Gold));
        Space(s, 6);
        s.AddChild(ActionButton("Return to the valley", () => { _pause.Hide(); Command?.Invoke("resume"); }, true));
        s.AddChild(ActionButton("Save your journey", () => Command?.Invoke("save")));
        s.AddChild(ActionButton("World sandbox settings",()=>Command?.Invoke("sandbox:open")));
        _soundButton = ActionButton("Sound: on", () => { SetSoundEnabled(!_sound); Command?.Invoke(_sound ? "sound:on" : "sound:off"); });
        s.AddChild(_soundButton);
        Space(s,4);
        s.AddChild(Text("CAMERA",11,Gold));
        _cameraMode=ActionButton("Rotation: mouse drag",()=>Command?.Invoke(_alwaysCamera?"camera:mode:drag":"camera:mode:always"));s.AddChild(_cameraMode);
        _cameraSensitivityLabel=Text("Mouse sensitivity  ·  1.0×",14,Muted);s.AddChild(_cameraSensitivityLabel);
        _cameraSensitivity=new HSlider{MinValue=.3,MaxValue=2.5,Step=.1,Value=1,CustomMinimumSize=new Vector2(0,24)};
        _cameraSensitivity.ValueChanged+=value=>{if(!_updatingCameraOptions)Command?.Invoke("camera:sensitivity:"+value.ToString("0.0",CultureInfo.InvariantCulture));};s.AddChild(_cameraSensitivity);
        _invertY=ActionButton("Invert vertical look: off",()=>Command?.Invoke(_invertedCamera?"camera:invert:off":"camera:invert:on"));s.AddChild(_invertY);
        var cameraHelp=Text("Drag: hold middle mouse or both buttons.\nAlways: move mouse to look; hold Tab for cursor.\nMenus and building release the cursor. Home resets view.",12,Muted);
        cameraHelp.AutowrapMode=TextServer.AutowrapMode.WordSmart;s.AddChild(cameraHelp);
        s.AddChild(ActionButton("Save and quit", () => Command?.Invoke("quit")));
        _pause.Hide();
    }

    public void ShowTitle(bool hasSave, int seed = 7319)
    {
        _title.Show(); _hud.Hide(); _panel.Hide(); _pause.Hide(); _collection.Hide(); HidePlayerScreens(); HideSandbox();
        _continue.Disabled = !hasSave;
        _seed.Text = seed.ToString();
    }
    public void ShowGame() { HideWorldSetup(); _title.Hide(); _hud.Show(); _pause.Hide(); _panel.Hide(); _collection.Hide(); HidePlayerScreens(); HideSandbox(); }
    public void SetSoundEnabled(bool enabled) { _sound = enabled; _soundButton.Text = "Sound: " + (enabled ? "on" : "off"); }
    public void SetCameraOptions(bool always,float sensitivity,bool inverted)
    {
        _updatingCameraOptions=true;
        _alwaysCamera=always;_invertedCamera=inverted;
        _cameraMode.Text=always?"Rotation: always follow mouse":"Rotation: mouse drag";
        _cameraSensitivity.Value=sensitivity;
        _cameraSensitivityLabel.Text=$"Mouse sensitivity  ·  {sensitivity:0.0}×";
        _invertY.Text=inverted?"Invert vertical look: on":"Invert vertical look: off";
        _updatingCameraOptions=false;
    }
    public void SetCameraState(bool captured,bool always)
    {
        _reticle.Visible=captured&&!IsPanelOpen;
        _cameraHint.Text=always?"Mouse look  ·  Hold Tab for cursor  ·  Esc for camera options":"Hold middle mouse or both mouse buttons to orbit  ·  Hold Q / R to turn";
    }
    public void UpdateVitals(float health, float stamina)
    {
        _health.Value = Mathf.Clamp(health, 0, 100); _stamina.Value = Mathf.Clamp(stamina, 0, 100);
        _healthNumber.Text = MathF.Ceiling(health).ToString(); _staminaNumber.Text = MathF.Ceiling(stamina).ToString();
    }
    public void UpdateWorld(string time, string resources, string rumor) { _time.Text = time; _resources.Text = resources; _rumor.Text = rumor; }
    public void SetContext(string text) => _context.Text = text;
    public void SetBuildHeight(string text){_buildHeightHint.Text=text;_buildHeightHint.Visible=text!=""&&!IsPanelOpen;}
    public void Notify(string text) { _notice.Text = text; _noticeTime = 4.5f; _notice.Modulate = Colors.White; }
    public void SetHotbar(int selected)
    {
        // Legacy tool-mode callers remain compatible; actual bindings are rendered by UpdateHotbar.
    }
    public void ShowPanel(string title, string body, IEnumerable<(string Label, string Command)> actions)
    {
        _pause.Hide();
        _collection.Hide();
        HidePlayerScreens();
        HideSandbox();
        _panelTitle.Text = title;
        foreach (Node c in _panelBody.GetChildren()) { _panelBody.RemoveChild(c); c.QueueFree(); }
        var description = Text(body, 16, Cream);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.AddThemeConstantOverride("line_spacing", 7);
        _panelBody.AddChild(description);
        Space(_panelBody, 8);
        foreach (var action in actions)
        {
            string command = action.Command;
            var button = ActionButton(action.Label, () => Command?.Invoke(command));
            button.Alignment = HorizontalAlignment.Left;
            _panelBody.AddChild(button);
        }
        _panel.Show();
    }
    public void HidePanel() { _panel.Hide(); _pause.Hide(); _collection.Hide(); HidePlayerScreens(); HideSandbox(); }
    public void ShowPause() { _panel.Hide(); _collection.Hide(); HidePlayerScreens(); HideSandbox(); _pause.Show(); }
    public override void _Process(double delta)
    {
        UpdateLandscapePreview(delta);
        UpdateBuildOptionsVisibility();
        Place(_notice,.5f,IsPanelOpen?.91f:.12f,-500,0,1000,44);
        if (_noticeTime <= 0) return;
        _noticeTime -= (float)delta;
        _notice.Modulate = new Color(1, 1, 1, Mathf.Clamp(_noticeTime, 0, 1));
    }

    private Control Modal(Color shade)
    {
        var overlay = Full(new Control { MouseFilter = Control.MouseFilterEnum.Stop });
        var dim = Full(new ColorRect { Color = shade, MouseFilter = Control.MouseFilterEnum.Ignore });
        overlay.AddChild(dim); _root.AddChild(overlay); return overlay;
    }
    private static T Full<T>(T control) where T : Control { control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); return control; }
    private static void Place(Control c, float x, float y, float left, float top, float width, float height)
    {
        c.AnchorLeft = c.AnchorRight = x; c.AnchorTop = c.AnchorBottom = y;
        c.OffsetLeft = left; c.OffsetTop = top; c.OffsetRight = left + width; c.OffsetBottom = top + height;
    }
    private static StyleBoxFlat Box(Color background, Color border)
    {
        return new StyleBoxFlat { BgColor = background, BorderColor = border, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 };
    }
    private static Panel Card(float w, float h)
    {
        var p = new Panel { CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Box(new Color(Ink, .93f), new Color("4f5947"))); return p;
    }
    private static VBoxContainer Stack(Control parent, int horizontal, int vertical, int separation)
    {
        var margin = Full(new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore });
        margin.AddThemeConstantOverride("margin_left", horizontal); margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical); margin.AddThemeConstantOverride("margin_bottom", vertical);
        parent.AddChild(margin);
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", separation); margin.AddChild(box); return box;
    }
    private Label Text(string text, int size, Color color, bool serif = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        if (serif) label.AddThemeFontOverride("font", _serif);
        return label;
    }
    private static Button ActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44), MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.All };
        if (primary) { button.AddThemeStyleboxOverride("normal", Box(new Color("bda873"), new Color("dfca93"))); button.AddThemeColorOverride("font_color", new Color("19221b")); }
        button.Pressed += action; return button;
    }
    private static void Space(VBoxContainer box, float height) => box.AddChild(new Control { CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore });
    private void AddVital(VBoxContainer parent, string name, Color color, out ProgressBar bar, out Label number)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
        var label = Text(name, 12, Muted); label.CustomMinimumSize = new Vector2(50, 0); row.AddChild(label);
        bar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(130, 6), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", Box(new Color("0f1712"), Colors.Transparent));
        var fill = Box(color, Colors.Transparent); fill.ContentMarginTop = fill.ContentMarginBottom = 0;
        bar.AddThemeStyleboxOverride("fill", fill); row.AddChild(bar);
        number = Text("100", 11, Muted); number.CustomMinimumSize = new Vector2(25, 0); number.HorizontalAlignment = HorizontalAlignment.Right; row.AddChild(number); parent.AddChild(row);
    }
}


