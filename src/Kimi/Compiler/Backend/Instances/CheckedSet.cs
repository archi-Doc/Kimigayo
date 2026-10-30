// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

/// <summary>The checked set of a program version (SPEC 8.15.3, IMPL 21.3.1), which only <see cref="InstanceCollector"/> writes: its items in
/// admission order, the roots first, each with its targets in its body's location order, and its instance-stage records, one per code and
/// subject, which <c>Compilation.MirRecords</c> publishes at check on every pipeline (DECISIONS BU-48, R5-M156); at emit, its Generated
/// column (K package 4.10), which the generation entry marks. Its arrays and lookup keep their storage, so collecting and marking a
/// program again allocate nothing.</summary>
internal sealed class CheckedSet
{
    private readonly Dictionary<MonoKey, int> lookup = new();
    private readonly HashSet<Problem> reported = new();
    private MonoItem[] items = [];
    private MonoTarget[] targets = [];
    private bool[] generated = []; // The Generated column, by item.
    private int[] marking = []; // Mark's worklist.
    private int itemCount;
    private int targetCount;

    /// <summary>Gets the program version collected (0: none).</summary>
    internal ulong Version { get; private set; }

    /// <summary>Gets the number of roots, the first items.</summary>
    internal int RootCount { get; private set; }

    /// <summary>Gets the records of the collection, one per code and subject: a capability gap's <c>Unsupported_Kd</c>, a limit's
    /// <c>GenerationResourceLimit_Kd</c>, a C-layout instance's <c>InvalidCLayout_Kd</c>, a layout over 2^63 - 1 bytes' <c>OversizedLayout_Kd</c>
    /// at a use (K 4.8), a broken contract's <c>InternalInvariant_Kd</c>, which only a complete Binding gives (DECISIONS R5-M151).</summary>
    internal List<Problem> Records { get; } = [];

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

    /// <summary>Gets whether code is generated for an item: whether the latest <see cref="Mark"/> of this version reached it.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Whether the item is Generated.</returns>
    internal bool IsGenerated(int item) => this.generated[item];

    /// <summary>Marks the Generated column (K package 4.10, IMPL 21.3.1): exactly the output roots and, transitively, every item a runtime
    /// target of a Generated item names; an item only the checks reach is checked, never generated through that target.</summary>
    /// <param name="roots">The output roots.</param>
    internal void Mark(ReadOnlySpan<int> roots)
    {
        this.generated.AsSpan(0, this.itemCount).Clear();
        if (this.marking.Length < this.itemCount)
        {
            this.marking = new int[this.items.Length];
        }

        var count = 0;
        foreach (var root in roots)
        {
            count = this.Generate(root, count);
        }

        while (count > 0)
        {
            foreach (var target in this.Targets(this.marking[--count]))
            {
                count = target.Runtime ? this.Generate(target.Item, count) : count;
            }
        }
    }

    internal void Clear(ulong version)
    {
        this.lookup.Clear();
        this.reported.Clear();
        this.Records.Clear();
        this.generated.AsSpan(0, this.itemCount).Clear();
        (this.itemCount, this.targetCount, this.RootCount, this.Version) = (0, 0, 0, version);
    }

    // A new item, whose key has none yet.
    internal int Add(in MonoItem entry)
    {
        if (this.itemCount == this.items.Length)
        {
            Array.Resize(ref this.items, Math.Max(64, this.itemCount * 2));
            Array.Resize(ref this.generated, this.items.Length);
        }

        var item = this.itemCount++;
        this.items[item] = entry;
        this.lookup.Add(entry.Key, item);
        return item;
    }

    // A record at its subject, once per code and subject.
    internal void Report(DiagnosticCode code, SourceRef subject)
    {
        if (this.reported.Add(new(code, subject)))
        {
            this.Records.Add(new(code, subject));
        }
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

    // An item Generated now, onto the worklist once.
    private int Generate(int item, int count)
    {
        if (!this.generated[item])
        {
            this.generated[item] = true;
            this.marking[count++] = item;
        }

        return count;
    }
}
