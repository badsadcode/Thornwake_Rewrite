using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.WorldGen;

/// <summary>
/// Generated objects taken out of the world (a felled tree first): a sparse set keyed by the object's layer and its
/// lattice cell, which is the object's identity (the same on every PC: the lattice comes from the seed). Like
/// <see cref="TerrainEdits"/> it is a world change: thread-safe for one writer (the game) and many readers (tile
/// builders), with the keys changed since the last <see cref="TakeChanged"/> (multiplayer: the host sends them).
/// The value is when it happened (game hours), for regrowth later.
/// </summary>
public sealed class ObjectRemovals
{
    /// <summary>Layers: one lattice of generated objects each (trees felled, loose stones picked up, berry bushes picked).</summary>
    public const int Trees = 0, Stones = 1, Berries = 2, Grass = 3;
    /// <summary>The grass layer's cells (m): pulling long grass bares the cells around the spot (D119).</summary>
    public const double GrassCell = 1;

    private readonly ConcurrentDictionary<(int Layer, long X, long Z), double> _removed = new();
    private readonly ConcurrentDictionary<(int Layer, long X, long Z), byte> _changed = new();

    /// <summary>Incremented by every change.</summary>
    public long Version { get; private set; }
    public int Count => _removed.Count;
    public bool IsEmpty => _removed.IsEmpty;

    public bool Contains(int layer, long x, long z) => !_removed.IsEmpty && _removed.ContainsKey((layer, x, z));

    /// <summary>Take the object out (at <paramref name="hours"/> game time). False if it was already gone.</summary>
    public bool Remove(int layer, long x, long z, double hours)
    {
        if (!_removed.TryAdd((layer, x, z), hours)) return false;
        _changed[(layer, x, z)] = 0;
        Version++;
        return true;
    }

    /// <summary>Put it back (regrowth, undo). False if it was there.</summary>
    public bool Restore(int layer, long x, long z)
    {
        if (!_removed.TryRemove((layer, x, z), out _)) return false;
        _changed[(layer, x, z)] = 0;
        Version++;
        return true;
    }

    /// <summary>The state of one key as the host sends it: removed at that time, or null for present.</summary>
    public double? Get(int layer, long x, long z) => _removed.TryGetValue((layer, x, z), out double h) ? h : null;

    /// <summary>Apply a key from the host (a joining player): not counted as a change, so not sent back.</summary>
    public void Set(int layer, long x, long z, double? removedAt)
    {
        if (removedAt is { } h) _removed[(layer, x, z)] = h; else _removed.TryRemove((layer, x, z), out _);
        Version++;
    }

    public List<(int Layer, long X, long Z)> Keys() => _removed.Keys.ToList();

    /// <summary>The keys changed since the last call (and forgets them).</summary>
    public List<(int Layer, long X, long Z)> TakeChanged()
    {
        var keys = _changed.Keys.ToList();
        foreach (var k in keys) _changed.TryRemove(k, out _);
        return keys;
    }

    /// <summary>Every key counts as changed (a loaded save: the host sends all of it).</summary>
    public void MarkAllChanged() { foreach (var k in _removed.Keys) _changed[k] = 0; }

    public void Clear()
    {
        foreach (var k in _removed.Keys) _changed[k] = 0;
        _removed.Clear();
        Version++;
    }
}
