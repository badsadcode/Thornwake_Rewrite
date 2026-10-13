using System;
using System.Globalization;
using System.Linq;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// --teleport-test=count,seconds: jump to <c>count</c> random land points (fixed seed: the same points every run), one
/// every <c>seconds</c>, like map jumps — to reproduce the long freezes after teleports (Tom, multiplayer session
/// 2026-10-13). Long frames are reported by Hitches; each jump prints TELEPORT with the place, and TELEPORT_DONE at the end.
/// </summary>
public partial class WorldLab
{
    private int _teleportsLeft = -1;
    private double _teleportEvery, _teleportTimer;
    private readonly Random _teleportRandom = new(1234);

    private void TeleportTest(double delta)
    {
        if (_sampler == null || _generation != null) return;
        if (_teleportsLeft < 0)
        {
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--teleport-test=")) is not { } arg) { _teleportsLeft = 0; return; }
            var v = arg[16..].Split(',');
            _teleportsLeft = int.Parse(v[0], CultureInfo.InvariantCulture);
            _teleportEvery = double.Parse(v[1], CultureInfo.InvariantCulture);
            _teleportTimer = _teleportEvery;
        }
        if (_teleportsLeft == 0) return;
        if ((_teleportTimer -= delta) > 0) return;
        _teleportTimer = _teleportEvery;
        double half = _sampler.Plan.Profile.WorldSize / 2;
        for (int tries = 0; tries < 200; tries++)
        {
            double x = (_teleportRandom.NextDouble() * 2 - 1) * half * .8, z = (_teleportRandom.NextDouble() * 2 - 1) * half * .8;
            var t = _sampler.Sample(x, z);
            if (t.Water != WaterKind.None || t.Height < 2) continue;
            GD.Print(string.Format(CultureInfo.InvariantCulture, "TELEPORT {0} to {1:0}, {2:0} (from {3:0}, {4:0})", _teleportsLeft, x, z, PlayerX, PlayerZ));
            Teleport(x, z);
            break;
        }
        if (--_teleportsLeft == 0) { _teleportTimer = _teleportEvery; GD.Print("TELEPORT_DONE"); }
    }
}
