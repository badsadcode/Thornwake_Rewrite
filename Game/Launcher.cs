using Godot;
using System.Globalization;
using System.Linq;
using Thornwake.Rewrite.Net;

namespace Thornwake;

/// <summary>
/// Start screen: choose the rewritten world preview or the original Harthwood valley game. Command-line runs of
/// the original game's smoke test (<c>-- --smoke-test</c>) go straight to the valley so existing tooling still works.
/// Multiplayer (spike, GAMEPLAY_DESIGN.md §14.0): host a game or join one by address, under a name of your choice.
/// </summary>
public partial class Launcher : Control
{
	private VBoxContainer _main = null!, _multi = null!;
	private LineEdit _name = null!, _address = null!;
	private SpinBox _port = null!;
	private Label _hint = null!;

	public override void _Ready()
	{
		Rewrite.DisplaySettings.Apply(GetTree().Root);
		var args = OS.GetCmdlineUserArgs();
		if (args.Any(a => a == "--smoke-test")) { CallDeferred(nameof(Open), "res://Game/Main.tscn"); return; }
		if (args.Any(a => a.StartsWith("--world-smoke=") || a.StartsWith("--bench=") || a == "--world" || a == "--collision-test")) { CallDeferred(nameof(Open), "res://Game/Rewrite/WorldLab.tscn"); return; }
		Input.MouseMode = Input.MouseModeEnum.Visible;
		SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(new ColorRect { Color = new Color("1f2b2d"), AnchorRight = 1, AnchorBottom = 1 });
		_main = Column(-200);
		var title = new Label { Text = "THORNWAKE", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 44);
		_main.AddChild(title);
		_main.AddChild(new Label { Text = "Rewrite preview build", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, .7f) });
		Add(_main, "Explore the new world (rewrite preview)", () => Open("res://Game/Rewrite/WorldLab.tscn"));
		Add(_main, "Multiplayer…", () => { _main.Visible = false; _multi.Visible = true; _name.GrabFocus(); });
		Add(_main, "Harthwood Valley (original game)", () => Open("res://Game/Main.tscn"));
		Add(_main, "Quit", () => GetTree().Quit(), 44);
		BuildMultiplayer();
		// --launcher-shot=<png>: a picture of the Multiplayer page (checking the layout), then quit.
		if (args.FirstOrDefault(a => a.StartsWith("--launcher-shot=")) is { } shot)
		{
			_main.Visible = false; _multi.Visible = true;
			GetTree().CreateTimer(1).Timeout += () => { GetViewport().GetTexture().GetImage().SavePng(shot[16..]); GetTree().Quit(); };
		}
	}

	private VBoxContainer Column(float top)
	{
		var box = new VBoxContainer { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f,
			OffsetLeft = -250, OffsetRight = 250, OffsetTop = top, OffsetBottom = -top };
		box.AddThemeConstantOverride("separation", 12);
		AddChild(box);
		return box;
	}

	/// <summary>The Multiplayer page: a name, then host (on a port) or join (an address and port). Remembered.</summary>
	private void BuildMultiplayer()
	{
		NetLaunch.Load();
		_multi = Column(-260);
		_multi.Visible = false;
		var title = new Label { Text = "Multiplayer", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 34);
		_multi.AddChild(title);
		_multi.AddChild(new Label { Text = "Your name" });
		_name = new LineEdit { Text = NetLaunch.Name, MaxLength = 24, PlaceholderText = "the name others see above you" };
		_multi.AddChild(_name);
		_multi.AddChild(new Label { Text = "Port (UDP)" });
		_port = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = NetLaunch.Port, Rounded = true };
		_multi.AddChild(_port);
		_multi.AddChild(new HSeparator());
		Add(_multi, "Host a game", Host);
		_multi.AddChild(new Label { Text = "Others join with this PC's address (your public IP over the internet, after\nforwarding the UDP port on your router; your local IP on the same network).",
			Modulate = new Color(1, 1, 1, .6f), AutowrapMode = TextServer.AutowrapMode.Off });
		_multi.AddChild(new HSeparator());
		_multi.AddChild(new Label { Text = "Host's address (IP or name; :port optional)" });
		_address = new LineEdit { Text = NetLaunch.Address, PlaceholderText = "e.g. 83.12.45.67 or 192.168.1.20:7777" };
		_address.TextSubmitted += _ => Join();
		_multi.AddChild(_address);
		Add(_multi, "Join", Join);
		_hint = new Label { Modulate = new Color(1, .75f, .5f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_multi.AddChild(_hint);
		Add(_multi, "Back", () => { _multi.Visible = false; _main.Visible = true; }, 40);
	}

	private bool Remember()
	{
		NetLaunch.Name = _name.Text.Trim();
		NetLaunch.Port = (int)_port.Value;
		NetLaunch.Address = _address.Text.Trim();
		if (NetLaunch.Name.Length == 0) { _hint.Text = "Choose a name first."; _name.GrabFocus(); return false; }
		NetLaunch.Save();
		return true;
	}

	private void Host()
	{
		if (!Remember()) return;
		NetLaunch.Pending = NetLaunch.Role.Host;
		Open("res://Game/Rewrite/WorldLab.tscn");
	}

	private void Join()
	{
		if (!Remember()) return;
		string address = NetLaunch.Address;
		if (address.Length == 0) { _hint.Text = "Type the host's address (they can find it as described under Host a game)."; _address.GrabFocus(); return; }
		// "address:port" overrides the port box (IPv6 addresses in brackets: [::1]:7777).
		int colon = address.LastIndexOf(':');
		if (colon > 0 && address.IndexOf(':') == colon && int.TryParse(address[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
		{
			NetLaunch.Address = address[..colon];
			NetLaunch.Port = port;
		}
		NetLaunch.Pending = NetLaunch.Role.Join;
		Open("res://Game/Rewrite/WorldLab.tscn");
	}

	private static void Add(Container box, string text, System.Action action, float height = 52)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, height) };
		button.Pressed += action;
		box.AddChild(button);
	}

	private void Open(string scene) => GetTree().ChangeSceneToFile(scene);
}
