// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

/// <summary>The checked set of a program version (SPEC 8.15.3, IMPL 21.3.1), which only <see cref="InstanceCollector"/> writes: its items in
/// admission order, the roots first, each with its targets in its body's location order. Its arrays and lookup keep their storage, so
/// collecting a program again allocates nothing.</summary>
internal sealed class CheckedSet
{
    private readonly Dictionary<MonoKey, int> lookup = new();
    private MonoItem[] items = [];
    private MonoTarget[] targets = [];
    private int itemCount;
    private int targetCount;

    /// <summary>Gets the program version collected (0: none).</summary>
    internal ulong Version { get; private set; }

    /// <summary>Gets the number of roots, the first items.</summary>
    internal int RootCount { get; private set; }

    /// <summary>Gets the items in admission order.</summary>
    internal ReadOnlySpan<MonoItem> Items => this.items.AsSpan(0, this.itemCount);

    /// <summary>Gets the targets of an item, in its body's location order.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The targets.</returns>
    internal ReadOnlySpan<MonoTarget> Targets(int item) => this.targets.AsSpan(this.items[item].FirstTarget, this.items[item].TargetCount);

    /// <summary>Finds the item of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The item, or -1.</returns>
    internal int Find(in MonoKey key) => this.lookup.TryGetValue(key, out var item) ? item : -1;

    internal void Clear(ulong version)
    {
        this.lookup.Clear();
        (this.itemCount, this.targetCount, this.RootCount, this.Version) = (0, 0, 0, version);
    }

    // The item of a key, admitted once.
    internal int Admit(in MonoKey key, int body)
    {
        if (!this.lookup.TryGetValue(key, out var item))
        {
            if (this.itemCount == this.items.Length)
            {
                Array.Resize(ref this.items, Math.Max(64, this.itemCount * 2));
            }

            this.items[item = this.itemCount++] = new(key, body, 0, 0);
            this.lookup.Add(key, item);
        }

        return item;
    }

    // The items admitted so far are the roots.
    internal void EndRoots() => this.RootCount = this.itemCount;

    // A target of the item being replayed: items are replayed in order, so each one's targets are contiguous.
    internal void Target(int item, in MonoTarget target)
    {
        if (this.targetCount == this.targets.Length)
        {
            Array.Resize(ref this.targets, Math.Max(64, this.targetCount * 2));
        }

        ref var entry = ref this.items[item];
        entry.FirstTarget = entry.TargetCount == 0 ? this.targetCount : entry.FirstTarget;
        entry.TargetCount++;
        this.targets[this.targetCount++] = target;
    }
}
