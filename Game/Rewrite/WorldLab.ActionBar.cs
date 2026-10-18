using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// A small progress bar over Alden's head while something takes time (Tom; D118): making, gathering, felling, cutting a
/// trunk, charging a cast — with a short text saying what (setting Interface → Action text). Each system calls
/// <see cref="ShowAction"/> every frame it is busy; the bar hides itself the first frame nobody does.
/// </summary>
public partial class WorldLab
{
    private PanelContainer? _actionBar;
    private ProgressBar? _actionProgress;
    private Label? _actionText;
    private bool _actionThisFrame;

    /// <summary>This frame Alden is busy with <paramref name="text"/>, <paramref name="progress"/> 0 … 1 done.</summary>
    private void ShowAction(string text, double progress)
    {
        if (_actionBar == null)
        {
            _actionBar = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(150, 0) };
            var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, .45f), ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 4 };
            style.SetCornerRadiusAll(4);
            _actionBar.AddThemeStyleboxOverride("panel", style);
            var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 1);
            _actionBar.AddChild(box);
            _actionText = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            _actionText.AddThemeFontSizeOverride("font_size", 13);
            box.AddChild(_actionText);
            _actionProgress = new ProgressBar { MaxValue = 1, Step = .001, ShowPercentage = false, CustomMinimumSize = new Vector2(138, 7), MouseFilter = Control.MouseFilterEnum.Ignore };
            var fill = new StyleBoxFlat { BgColor = new Color(.93f, .8f, .45f) };
            var back = new StyleBoxFlat { BgColor = new Color(.15f, .15f, .15f, .8f) };
            _actionProgress.AddThemeStyleboxOverride("fill", fill);
            _actionProgress.AddThemeStyleboxOverride("background", back);
            box.AddChild(_actionProgress);
            _hud.GetParent().AddChild(_actionBar);
        }
        _actionThisFrame = true;
        _actionText!.Text = text;
        _actionText.Visible = GameSettings.ActionText.On;
        _actionProgress!.Value = Mathf.Clamp(progress, 0, 1);
    }

    /// <summary>After everything ran this frame: placed over the head (in third person), or hidden.</summary>
    private void UpdateActionBar()
    {
        if (_actionBar == null) return;
        bool show = _actionThisFrame;
        _actionThisFrame = false;
        var head = _player.Position + Vector3.Up * (FirstPerson ? 1.1f : 2.3f) + (FirstPerson ? -_camera.GlobalTransform.Basis.Z * 1.5f : Vector3.Zero);
        if (!show || _camera.IsPositionBehind(head)) { _actionBar.Visible = false; return; }
        _actionBar.Visible = true;
        _actionBar.ResetSize();
        var at = _camera.UnprojectPosition(head);
        _actionBar.Position = at - new Vector2(_actionBar.Size.X / 2, _actionBar.Size.Y);
    }
}
