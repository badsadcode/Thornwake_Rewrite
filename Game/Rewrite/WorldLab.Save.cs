using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Saving and loading the world (D116): Esc → Save / Load…, F5 a quick save, an autosave every ten minutes (alone or
/// hosting; a joined player does not save). A save is the world's arguments plus every change (WorldSave); loading builds
/// the world again from its seed and lays the changes back on, then puts the host's player where they were. Files in
/// user://saves (%APPDATA%\Thornwake\saves).
/// </summary>
public partial class WorldLab
{
    private WorldSave? _pendingSave;
    private double _autosaveTimer = 600;
    private PanelContainer? _loadPanel;
    private ItemList? _loadList;
    private List<string> _loadFiles = new();

    private static string SaveFolder
    {
        get
        {
            string dir = ProjectSettings.GlobalizePath("user://saves");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private bool CanSave(out string why)
    {
        why = IsNetClient ? "Only the host saves the world." : _sampler == null || _generation != null || _rules == null ? "The world is still being built." : "";
        return why.Length == 0;
    }

    /// <summary>Write the world to a file named after <paramref name="slot"/> (quick, auto, or a time stamp).</summary>
    private void SaveWorld(string slot)
    {
        if (!CanSave(out var why)) { _status = why; return; }
        var save = new WorldSave
        {
            WorldArgs = _worldArgs, WorldTypeJson = _worldTypeJson, Day = _clock.Day, Phase = _clock.Phase, CloudDrift = _atmosphere.CloudDrift,
            Settings = GameSettings.All.Where(GameSettings.AffectsWorld).Select(s => (s.Key, s.Value)).ToList(),
            Label = $"{_sampler!.Plan.Profile.Name}, seed {_sampler.Plan.Manifest.Seed} — {_clock.Describe()}",
        };
        save.Capture(_sampler.Edits, _sampler.Removals, _rules!.Objects);
        foreach (var (name, bytes) in _rules.SaveInventories()) save.Players[name] = new PlayerSave { Inventory = bytes };
        if (!save.Players.TryGetValue(GameRules.HostName, out var me)) save.Players[GameRules.HostName] = me = new PlayerSave();
        me.HasBody = true; me.X = PlayerX; me.Y = _player.Position.Y; me.Z = PlayerZ; me.Facing = _me.Facing;
        me.Food = _needs.Food; me.Warmth = _needs.Warmth; me.Wet = _needs.Wet; me.Health = _needs.Health;
        string path = Path.Combine(SaveFolder, slot + ".twsave");
        using (var file = File.Create(path)) save.Write(file);
        _status = $"World saved ({slot}: {save.Edits.Count} edit chunks, {save.Removals.Count} trees/stones/bushes taken, {_rules.Objects.All.Count} objects).";
        _net?.Stats.Event("saved " + path);
        GD.Print("SAVED " + path);
    }

    /// <summary>Load a save: the world rebuilt from its arguments (everyone joined builds it too), then the changes.</summary>
    private void LoadSave(string path)
    {
        if (IsNetClient) { _status = "Only the host loads a world."; return; }
        WorldSave save;
        try { using var file = File.OpenRead(path); save = WorldSave.Read(file); }
        catch (Exception e) { _status = "Could not load: " + e.Message; return; }
        // The world's rules are the save's (sandbox settings live with the world).
        foreach (var (key, value) in save.Settings)
            if (GameSettings.All.FirstOrDefault(s => s.Key == key) is { } s && GameSettings.AffectsWorld(s)) s.Value = value;
        _pendingSave = save;
        StopFishing(null);
        ((IClientGame)this).LoadWorld(save.WorldArgs, save.WorldTypeJson, null);
        _status = "Loading " + Path.GetFileNameWithoutExtension(path) + "…";
        GD.Print("LOADING " + path);
    }

    /// <summary>After the world is built again: the save's changes, the clock, the clouds, the packs, and the host's player.</summary>
    private void ApplyPendingSave()
    {
        if (_pendingSave is not { } save || _sampler == null || _rules == null) return;
        _pendingSave = null;
        save.Restore(_sampler.Edits, _sampler.Removals, _rules.Objects);
        // Everyone (the host's own view included) is sent all of it: as changes.
        _sampler.Edits.MarkAllChanged();
        _sampler.Removals.MarkAllChanged();
        foreach (var o in _rules.Objects.All) _rules.Objects.Touch(o.Id);
        _streamer.Refresh();
        _rules.RestoreInventories(save.Players.ToDictionary(p => p.Key, p => p.Value.Inventory));
        _rules.Name((int)NetServer.HostPeer, GameRules.HostName);
        _clock.Follow(save.Day, save.Phase, 1, snap: true);
        _atmosphere.FollowCloudDrift(save.CloudDrift);
        if (save.Players.TryGetValue(GameRules.HostName, out var me) && me.HasBody)
        {
            Teleport(me.X, me.Z);
            _me.Facing = me.Facing;
            _needs.Food = me.Food; _needs.Warmth = me.Warmth; _needs.Wet = me.Wet; _needs.Health = Math.Max(me.Health, 1);
        }
        _status = $"Loaded: {save.Label} (saved {save.Saved.ToLocalTime():yyyy-MM-dd HH:mm}).";
        GD.Print("LOADED " + save.Label);
    }

    private void UpdateAutosave(double delta)
    {
        if (!CanSave(out _)) return;
        if ((_autosaveTimer -= delta) > 0) return;
        _autosaveTimer = 600;
        SaveWorld("autosave");
    }

    /// <summary>Esc → Load…: the saves, newest first.</summary>
    private void ShowLoadPanel()
    {
        if (_loadPanel == null)
        {
            _loadPanel = new PanelContainer { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = -200, OffsetBottom = 200 };
            var box = new VBoxContainer();
            _loadPanel.AddChild(box);
            box.AddChild(new Label { Text = "Load a saved world" });
            _loadList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 280) };
            _loadList.ItemActivated += i => { _loadPanel.Visible = false; ToggleMenu(false); LoadSave(_loadFiles[(int)i]); };
            box.AddChild(_loadList);
            var row = new HBoxContainer();
            var load = new Button { Text = "Load" };
            load.Pressed += () => { if (_loadList.GetSelectedItems() is { Length: > 0 } s) { _loadPanel.Visible = false; ToggleMenu(false); LoadSave(_loadFiles[s[0]]); } };
            var close = new Button { Text = "Cancel" };
            close.Pressed += () => _loadPanel.Visible = false;
            row.AddChild(load); row.AddChild(close);
            box.AddChild(row);
            _menu.GetParent().AddChild(_loadPanel);
        }
        _loadFiles = Directory.GetFiles(SaveFolder, "*.twsave").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        _loadList!.Clear();
        foreach (var f in _loadFiles)
        {
            string label = Path.GetFileNameWithoutExtension(f);
            try { using var file = File.OpenRead(f); label = $"{label} — {WorldSave.Read(file).Label}"; } catch { label += " (unreadable)"; }
            _loadList.AddItem($"{label}   ({File.GetLastWriteTime(f):yyyy-MM-dd HH:mm})");
        }
        if (_loadFiles.Count == 0) _loadList.AddItem("(no saves yet: Esc → Save, or F5)");
        _loadPanel.Visible = true;
    }

    /// <summary>The menu's buttons (Save, Load…).</summary>
    private void AddSaveButtons(HBoxContainer bar)
    {
        var save = new Button { Text = "Save", TooltipText = "Save the world (F5: a quick save; an autosave every ten minutes)." };
        save.Pressed += () => { SaveWorld(DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)); ToggleMenu(false); };
        var load = new Button { Text = "Load…" };
        load.Pressed += ShowLoadPanel;
        bar.AddChild(save); bar.AddChild(load);
    }
}
