using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.Gameplay;

/// <summary>
/// What a character carries: item stacks in order of arrival. No slot limit yet; the weight is there for encumbrance
/// later (§2). The host keeps the true one; a player's own PC shows a copy the host sends after every change.
/// </summary>
public sealed class Inventory
{
    private readonly Catalog _catalog;
    private readonly List<(string Item, int Count)> _stacks = new();

    public Inventory(Catalog catalog) => _catalog = catalog;

    public IReadOnlyList<(string Item, int Count)> Stacks => _stacks;
    public double Weight => _stacks.Sum(s => _catalog.Item(s.Item).Weight * s.Count);

    public int Count(string item) => _stacks.Where(s => s.Item == item).Sum(s => s.Count);
    public int CountTag(string tag) => _stacks.Where(s => _catalog.Item(s.Item).Has(tag)).Sum(s => s.Count);
    public int Count(Ingredient x) => _stacks.Where(s => x.Matches(_catalog.Item(s.Item))).Sum(s => s.Count);
    public bool HasTag(string tag) => CountTag(tag) > 0;

    public void Add(string item, int count = 1)
    {
        int stack = Math.Max(1, _catalog.Item(item).Stack);
        for (int i = 0; i < _stacks.Count && count > 0; i++)
        {
            if (_stacks[i].Item != item || _stacks[i].Count >= stack) continue;
            int put = Math.Min(count, stack - _stacks[i].Count);
            _stacks[i] = (item, _stacks[i].Count + put);
            count -= put;
        }
        while (count > 0) { int put = Math.Min(count, stack); _stacks.Add((item, put)); count -= put; }
    }

    /// <summary>Take <paramref name="x"/>'s count of matching items (newest stacks first), or nothing if there are too
    /// few. Returns what was taken.</summary>
    public List<(string Item, int Count)>? Take(Ingredient x)
    {
        if (Count(x) < x.Count) return null;
        var taken = new List<(string, int)>();
        int left = x.Count;
        for (int i = _stacks.Count - 1; i >= 0 && left > 0; i--)
        {
            var (item, n) = _stacks[i];
            if (!x.Matches(_catalog.Item(item))) continue;
            int t = Math.Min(n, left);
            left -= t;
            taken.Add((item, t));
            if (t == n) _stacks.RemoveAt(i); else _stacks[i] = (item, n - t);
        }
        return taken;
    }

    public bool Take(string item, int count = 1) => Take(new Ingredient(item, null, count)) != null;

    public void Clear() => _stacks.Clear();

    // ---- network: the host sends the whole inventory (a few dozen bytes) after every change.
    public void Write(BinaryWriter w)
    {
        w.Write((ushort)_stacks.Count);
        foreach (var (item, count) in _stacks) { w.Write(item); w.Write(count); }
    }

    public void Read(BinaryReader r)
    {
        _stacks.Clear();
        int n = r.ReadUInt16();
        for (int i = 0; i < n; i++)
        {
            string item = r.ReadString(); int count = r.ReadInt32();
            if (_catalog.Items.ContainsKey(item) && count > 0) _stacks.Add((item, count)); // (an unknown item: another version)
        }
    }
}
