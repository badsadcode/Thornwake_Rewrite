using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;

namespace Thornwake.Rewrite;

/// <summary>
/// The build menu (Tom, build 63: "a nice GUI with previews of the elements I'm building"; D123): along the bottom of
/// the screen while building — the categories as tabs (number keys), the current category's shapes as cards with a
/// picture of each piece (V cycles them; with the cursor free — Tab — a click picks one), its materials (X), the chosen
/// piece's cost against what you carry, and what the ghost is doing (snapped, free and raised, or why it cannot go there).
/// </summary>
public partial class WorldLab
{
    private PanelContainer? _buildPanel;
    private HBoxContainer? _buildTabs, _buildCards, _buildMats;
    private RichTextLabel? _buildInfo;
    private Label? _buildHints;
    private PieceIcons? _pieceIcons;
    private string _menuShown = "";
    private bool _iconsChanged;

    private static readonly Color Gold = new(.93f, .8f, .45f), Faint = new(1, 1, 1, .55f);

    private void InitBuilding(CanvasLayer layer)
    {
        _pieceIcons = new PieceIcons(PieceModel);
        _pieceIcons.Changed += () =>
        {
            _iconsChanged = true;
            if (_pieceMenu != null && _pieceMenuId != 0) OpenPieceMenu(_pieceMenuId); // (its pictures as they arrive)
        };
        AddChild(_pieceIcons);
        _buildPanel = new PanelContainer { Visible = false, AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Begin, OffsetTop = -150, OffsetBottom = -150 };
        _buildPanel.AddThemeStyleboxOverride("panel", Box(new Color(.06f, .07f, .07f, .72f), 8, 10));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        _buildPanel.AddChild(rows);
        _pickTitle = new Label { Text = "Choose a piece: click its picture (the tabs change the category) · Tab or Esc: back to building", Visible = false };
        _pickTitle.AddThemeColorOverride("font_color", Gold);
        rows.AddChild(_pickTitle);
        _buildTabs = new HBoxContainer();
        _buildTabs.AddThemeConstantOverride("separation", 4);
        rows.AddChild(_buildTabs);
        var middle = new HBoxContainer();
        middle.AddThemeConstantOverride("separation", 14);
        rows.AddChild(middle);
        _buildCards = new HBoxContainer();
        _buildCards.AddThemeConstantOverride("separation", 6);
        middle.AddChild(_buildCards);
        _buildMats = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _buildMats.AddThemeConstantOverride("separation", 4);
        middle.AddChild(_buildMats);
        _buildInfo = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(700, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _buildInfo.AddThemeFontSizeOverride("normal_font_size", 15);
        rows.AddChild(_buildInfo);
        _buildHints = new Label { Text = "Tab: choose with the mouse · V shape · X material · P snap points · R / wheel turn (Shift finer) · PgUp/PgDn or Alt+wheel raise · " +
            "Ctrl: no snapping · LMB place · E bring materials · RMB on a piece: change it, turn it, take it down · MMB take down · B close",
            MouseFilter = Control.MouseFilterEnum.Ignore };
        _buildHints.AddThemeFontSizeOverride("font_size", 12);
        _buildHints.AddThemeColorOverride("font_color", Faint);
        rows.AddChild(_buildHints);
        layer.AddChild(_buildPanel);
    }

    private static StyleBoxFlat Box(Color bg, int radius, int margin, Color? border = null, int width = 0)
    {
        var s = new StyleBoxFlat { BgColor = bg, ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * .6f, ContentMarginBottom = margin * .6f };
        s.SetCornerRadiusAll(radius);
        if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(width); }
        return s;
    }

    private bool _helpBeforeBuilding = true;
    /// <summary>Choosing with the mouse (Tab while building; Tom): the cursor free over the menu, a click picks and goes back.</summary>
    private bool _buildPicking;
    private Label? _pickTitle;

    private void SetPicking(bool on)
    {
        _buildPicking = on && _building;
        ClosePieceMenu();
        Input.MouseMode = _buildPicking ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        _buildLmbWas = true; // (the click that picked is still held: it must not place — Tom, build 65; the next click does)
        if (_pickTitle != null) _pickTitle.Visible = _buildPicking;
        _buildPanel?.AddThemeStyleboxOverride("panel", Box(new Color(.06f, .07f, .07f, _buildPicking ? .88f : .72f), 8, 10, _buildPicking ? Gold : null, _buildPicking ? 1 : 0));
    }

    // ---- the right-click menu on a placed piece: another shape of its category, turned round, taken down, supplied
    private PanelContainer? _pieceMenu;
    private long _pieceMenuId;

    private void OpenPieceMenu(long id)
    {
        if (_seenObjects.Get(id) is not { } o || _catalog.Piece(o.Kind) is not { } def) return;
        ClosePieceMenu();
        _buildPicking = false;
        if (_pickTitle != null) _pickTitle.Visible = false;
        _pieceMenuId = id;
        _pieceMenu = new PanelContainer { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f, GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both };
        _pieceMenu.AddThemeStyleboxOverride("panel", Box(new Color(.06f, .07f, .07f, .9f), 8, 12, Gold, 1));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        _pieceMenu.AddChild(rows);
        string state = o.Blueprint ? "blueprint, needs " + NeedsHere(def, o) : "built";
        var title = new Label { Text = def.Name + " (" + state + ")" };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", Gold);
        rows.AddChild(title);
        rows.AddChild(new Label { Text = o.Blueprint ? "Change it to:" : "Change it to (its materials come back and go into the new one):" });
        // Every shape of its category, in every material (a log roof can become a thatch one).
        foreach (var material in _catalog.Pieces.Values.Where(p => p.Category == def.Category).Select(p => p.Material).Distinct())
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            foreach (var p in _catalog.Pieces.Values.Where(p => p.Category == def.Category && p.Material == material && _catalog.Family(p)[0] == p))
            {
                // (one card per shape; the change keeps the size: a diagonal wall becomes a diagonal doorway)
                var piece = _catalog.Family(p).FirstOrDefault(m => System.Math.Abs(m.Length - def.Length) < .05) ?? p;
                line.AddChild(Card(piece, piece.Family == def.Family, () =>
                {
                    if (piece.Id != def.Id) Act(new ActionRequest(ActionKind.Place, id, 0, piece.Id, 0, 0, 0, o.Yaw));
                    ClosePieceMenu(true);
                }));
            }
            rows.AddChild(line);
        }
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        rows.AddChild(buttons);
        void Button(string text, System.Action act) { var b = new Button { Text = text }; b.Pressed += () => { act(); ClosePieceMenu(true); }; buttons.AddChild(b); }
        Button("Turn it round", () => Act(new ActionRequest(ActionKind.Place, id, 0, def.Id, 0, 0, 0, o.Yaw + Mathf.Pi)));
        if (o.Blueprint) Button("Bring materials", () => Act(new ActionRequest(ActionKind.Supply, id, 0, "", 0, 0, 0, 0)));
        Button("Take it down", () => Act(new ActionRequest(ActionKind.Dismantle, id, 0, "", 0, 0, 0, 0)));
        Button("Close (Esc)", () => { });
        _buildPanel!.GetParent().AddChild(_pieceMenu);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void ClosePieceMenu(bool recapture = false)
    {
        if (_pieceMenu == null) return;
        _pieceMenu.QueueFree();
        _pieceMenu = null;
        _pieceMenuId = 0;
        if (recapture) { Input.MouseMode = Input.MouseModeEnum.Captured; _buildLmbWas = true; }
    }

    private void ShowBuildMenu(bool on)
    {
        if (_buildPanel != null) _buildPanel.Visible = on;
        // Every piece's picture made now (two frames each), so no card waits when its category or a piece's menu opens.
        if (on && _pieceIcons != null) foreach (var p in _catalog.Pieces.Values) _pieceIcons.Get(p);
        _menuShown = "";
        // The key help (F1) gives way to the menu while building, and comes back after.
        if (on) { _helpBeforeBuilding = _help.Visible; _help.Visible = false; }
        else _help.Visible = _helpBeforeBuilding;
    }

    /// <summary>Per frame while building: the tabs, cards and materials rebuilt when the choice changes; the text always.</summary>
    private void UpdateBuildMenu(PieceDef def)
    {
        if (_buildPanel == null || _buildTabs == null || _buildCards == null || _buildMats == null || _buildInfo == null) return;
        string key = $"{_buildCategory}|{Material}|{Shape}";
        if (key != _menuShown || _iconsChanged)
        {
            _menuShown = key; _iconsChanged = false;
            Fill(_buildTabs, _catalog.Categories.Select((c, i) => Chip($"{i + 1}  {c}", i == _buildCategory, () => { _buildCategory = i; })));
            var shapes = Shapes;
            Fill(_buildCards, shapes.Select(s => Card(InCategory.First(p => p.Material == Material && p.Shape == s), s == Shape)));
            Fill(_buildMats, Materials.Count > 1 ? Materials.Select(m => Chip(m, m == Material, () => _buildMaterial[Category] = m)) : Enumerable.Empty<Control>());
        }
        // Centred, its bottom 24 px above the screen's (the help text hides while building); every frame, as labels settle.
        var size = _buildPanel.GetCombinedMinimumSize();
        _buildPanel.OffsetLeft = -size.X / 2; _buildPanel.OffsetRight = size.X / 2;
        _buildPanel.OffsetTop = -24 - size.Y; _buildPanel.OffsetBottom = -24;
        // Cost: what you carry of each (red when short), then where the ghost is.
        var cost = string.Join(", ", def.Cost.Select(c =>
        {
            int have = _myInv.Count(c.Item!);
            string col = have >= c.Count ? "#e8dfc8" : "#ff8a70";
            return $"[color={col}]{c.Count} {_catalog.Item(c.Item!).Name.ToLowerInvariant()}[/color] [color=#ffffff80]({have})[/color]";
        }));
        string state = _ghostWhy.Length > 0 ? $"[color=#ff8a70]{_ghostWhy}[/color]"
            : _ghostReplace != 0 ? "[color=#9fd3ff]swap for this shape — its materials come back[/color]"
            : _ghostFree ? $"[color=#9fd3ff]free{(_buildRaise != 0 ? $", raised {_buildRaise:+0.00;-0.00} m" : "")} — LMB places the blueprint[/color]"
            : "[color=#9fd3ff]snapped — LMB places the blueprint[/color]";
        _buildInfo.Text = $"[b]{def.Name}[/b]   needs {cost}   ·   {state}";
    }

    /// <summary>The F9 dump's gameplay part (Tom's building reports): what is held, the building state and the ghost, the
    /// pieces within 25 m (kind, place, turn, blueprint), the animals near and the pack.</summary>
    private string GameplayDump(System.Func<double, string> f)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Holding: {(_axeHeld ? "axe" : _me.RodHeld ? "rod" : _me.Weapon ?? "nothing")} · building {(_building ? "open" : "closed")}" +
            (_building ? $" · {Category} / {Material} / {Shape} ({Piece.Id}) · turn {f(_buildTurn * 180 / System.Math.PI)}° · raise {f(_buildRaise)} m · " +
                $"ghost {(_ghostPose is { } g ? $"at {f(g.X)}, {f(g.Y)}, {f(g.Z)} yaw {f(g.Yaw * 180 / System.Math.PI)}° {(_ghostFree ? "free" : _ghostReplace != 0 ? $"swap {_ghostReplace}" : "snapped")}" : "none")}" +
                (_ghostWhy.Length > 0 ? $" — {_ghostWhy}" : "") : ""));
        var pieces = _seenObjects.Near(PlayerX, PlayerZ, 25).Where(o => _catalog.Piece(o.Kind) != null).ToList();
        sb.AppendLine($"Pieces within 25 m: {pieces.Count}{(_aimedAny != 0 ? $" · aimed at {_aimedAny}" : "")}");
        foreach (var o in pieces.Take(80))
            sb.AppendLine($"  {o.Id} {o.Kind} at {f(o.X)}, {f(o.Y)}, {f(o.Z)} yaw {f(o.Yaw * 180 / System.Math.PI)}°{(o.Blueprint ? " blueprint" : "")}{(o.State != 0 ? $" state {o.State}" : "")}");
        sb.AppendLine($"Animals near: {_animalViews.Count} ({string.Join(", ", _animalViews.Values.GroupBy(v => v.Def.Id).Select(g => $"{g.Key} {g.Count()}"))})");
        sb.AppendLine("Pack: " + string.Join(", ", _myInv.Stacks.Select(s => $"{s.Item} {s.Count}")));
        return sb.ToString();
    }

    private static void Fill(Container box, IEnumerable<Control> items)
    {
        foreach (var child in box.GetChildren()) { box.RemoveChild(child); child.QueueFree(); } // (out at once: not counted in the size)
        foreach (var c in items) box.AddChild(c);
    }

    /// <summary>A small labelled tab (a category, a material); clickable with the cursor free.</summary>
    private Control Chip(string text, bool on, System.Action pick)
    {
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        chip.AddThemeStyleboxOverride("panel", Box(on ? new Color(.93f, .8f, .45f, .22f) : new Color(1, 1, 1, .06f), 5, 8, on ? Gold : null, on ? 1 : 0));
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", on ? Gold : Faint);
        chip.AddChild(label);
        chip.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { pick(); _menuShown = ""; } };
        return chip;
    }

    /// <summary>A shape's card: its picture (rendered from its model) and name; gold when chosen.</summary>
    private Control Card(PieceDef def, bool on, System.Action? click = null)
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = def.Name };
        card.AddThemeStyleboxOverride("panel", Box(on ? new Color(.93f, .8f, .45f, .16f) : new Color(1, 1, 1, .05f), 6, 4, on ? Gold : new Color(1, 1, 1, .12f), on ? 2 : 1));
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        card.AddChild(column);
        var picture = new TextureRect { Texture = _pieceIcons?.Get(def), CustomMinimumSize = new Vector2(84, 84), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(picture);
        var label = new Label { Text = def.Shape, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(88, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", on ? Gold : new Color(1, 1, 1, .8f));
        column.AddChild(label);
        card.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            if (click != null) { click(); return; }
            // Picking (Tab): this piece, and back to placing it.
            _buildMaterial[Category] = def.Material; _buildShape[Category] = def.Shape; _menuShown = "";
            SetPicking(false);
        };
        return card;
    }
}
