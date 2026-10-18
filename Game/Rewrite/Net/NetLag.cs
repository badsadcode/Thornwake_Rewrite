using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite.Net;

/// <summary>
/// A slow, lossy line simulated on one PC (the real multiplayer test, D138: Tom's friends are ~320 ms away, the test PC
/// talks to itself in 0.1 ms). <c>--net-lag=ms[,loss%]</c>: everything this PC sends waits <c>ms</c> before it goes out
/// (in order, so reliable messages stay in order); unreliable ones (positions, animals, the clock) are also dropped at
/// <c>loss%</c>. Given to the host and the joined PCs alike, a round trip takes about twice <c>ms</c>.
/// </summary>
public sealed class NetLag
{
    private readonly double _delay, _loss;
    private readonly Queue<(ulong Due, long Peer, byte[] Data, bool Reliable)> _queue = new();
    private readonly Random _rng = new(11);

    public NetLag(double delayMs, double lossPercent) { _delay = delayMs; _loss = lossPercent; }

    public static NetLag FromArgs()
    {
        var arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--net-lag="));
        if (arg == null) return new NetLag(0, 0);
        var v = arg[10..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
        return new NetLag(v[0], v.Length > 1 ? v[1] : 0);
    }

    public bool On => _delay > 0 || _loss > 0;

    /// <summary>True when the message is held (or lost); false when it should go out now.</summary>
    public bool Hold(long peer, byte[] data, bool reliable)
    {
        if (!On) return false;
        if (!reliable && _loss > 0 && _rng.NextDouble() * 100 < _loss) return true;
        _queue.Enqueue((Time.GetTicksMsec() + (ulong)_delay, peer, data, reliable));
        return true;
    }

    /// <summary>Per frame: send what has waited long enough.</summary>
    public void Release(Action<long, byte[], bool> send)
    {
        ulong now = Time.GetTicksMsec();
        while (_queue.Count > 0 && _queue.Peek().Due <= now)
        {
            var (_, peer, data, reliable) = _queue.Dequeue();
            send(peer, data, reliable);
        }
    }
}
