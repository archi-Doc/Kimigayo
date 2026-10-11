// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using Kimi.Checking;

#pragma warning disable SA1402 // The input model is one vocabulary.

namespace Kimi.Lsp;

/// <summary>The kind of an input (SPEC 23.3.4).</summary>
internal enum InputKind : byte
{
    /// <summary>A source, project or lock file.</summary>
    File,

    /// <summary>A directory listing with a pattern.</summary>
    Listing,
}

/// <summary>Identifies one input: a file, or a directory listing with its pattern.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Identity">The file or directory.</param>
/// <param name="Pattern">The listing pattern, or empty for a file.</param>
internal readonly record struct InputKey(InputKind Kind, SourceIdentity Identity, string Pattern)
{
    /// <summary>The discovery pattern.</summary>
    public const string SourcePattern = "*.kimi";

    /// <summary>The candidate pattern.</summary>
    public const string ProjectPattern = "*.kimiproj";

    /// <summary>Creates a file key.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The key.</returns>
    public static InputKey File(string path) => new(InputKind.File, SourceIdentity.FromPath(path), string.Empty);

    /// <summary>Creates a file key.</summary>
    /// <param name="identity">The file identity.</param>
    /// <returns>The key.</returns>
    public static InputKey File(SourceIdentity identity) => new(InputKind.File, identity, string.Empty);

    /// <summary>Creates a listing key.</summary>
    /// <param name="directory">The directory path.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The key.</returns>
    public static InputKey Listing(string directory, string pattern) => new(InputKind.Listing, SourceIdentity.FromPath(directory), pattern);

    /// <summary>Gets the listing key of a file's directory whose pattern matches the file, if any.</summary>
    /// <param name="identity">The file.</param>
    /// <param name="key">The listing key.</param>
    /// <returns><see langword="true"/> when a pattern matches.</returns>
    public static bool TryGetListing(SourceIdentity identity, out InputKey key)
    {
        key = default;
        var path = identity.Value;
        var pattern = path.EndsWith(".kimi", StringComparison.OrdinalIgnoreCase) ? SourcePattern :
            path.EndsWith(".kimiproj", StringComparison.OrdinalIgnoreCase) ? ProjectPattern : null;
        if (pattern is null || Path.GetDirectoryName(path) is not { } directory)
        {
            return false;
        }

        key = Listing(directory, pattern);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => this.Kind == InputKind.File ? this.Identity.Value : Path.Combine(this.Identity.Value, this.Pattern);
}

/// <summary>An immutable observation of one input: its identity (SPEC 23.3.4) and the disk facts that drive re-validation.</summary>
internal sealed class InputState
{
    /// <summary>Gets a value indicating whether the input is established: readable and synchronized.</summary>
    public bool Established { get; init; } = true;

    /// <summary>Gets the observed failure of an unestablished input: its kind and message.</summary>
    public string? Failure { get; init; }

    /// <summary>Gets a value indicating whether an established file is absent.</summary>
    public bool Absent { get; init; }

    /// <summary>Gets the content of a present file: disk bytes, or the text of an open document.</summary>
    public SourceContent? Content { get; init; }

    /// <summary>Gets the names of a listing: its disk names plus the matching open documents, as full paths in ordinal order.</summary>
    public string[]? Names { get; init; }

    /// <summary>Gets the disk names of a listing, retained for reuse while the directory is unchanged.</summary>
    public string[]? DiskNames { get; init; }

    /// <summary>Gets a value indicating whether the content comes from an open document.</summary>
    public bool Overlay { get; init; }

    /// <summary>Gets the disk timestamp in ticks of the file or directory, or 0 when unknown.</summary>
    public long Stamp { get; init; }

    /// <summary>Gets the disk length of a file, or -1 when unknown.</summary>
    public long Length { get; init; } = -1;

    /// <summary>Creates the state of an unestablished input.</summary>
    /// <param name="failure">The failure kind and message.</param>
    /// <param name="stamp">The disk timestamp, if known.</param>
    /// <param name="overlay">Whether an open document overrides the file.</param>
    /// <param name="length">The disk length, if known.</param>
    /// <returns>The state.</returns>
    public static InputState Unestablished(string failure, long stamp = 0, bool overlay = false, long length = -1)
        => new() { Established = false, Failure = failure, Stamp = stamp, Overlay = overlay, Length = length };

    /// <summary>Compares the whole identity: establishment and failure, or bytes, names or absence.</summary>
    /// <param name="other">The other state.</param>
    /// <returns><see langword="true"/> when a check would observe the same input.</returns>
    public bool SameIdentity(InputState? other)
    {
        if (other is null || this.Established != other.Established)
        {
            return false;
        }

        if (!this.Established)
        {
            return string.Equals(this.Failure, other.Failure, StringComparison.Ordinal);
        }

        if (this.Absent || other.Absent)
        {
            return this.Absent == other.Absent;
        }

        if (this.Names is { } names)
        {
            return other.Names is { } otherNames && names.AsSpan().SequenceEqual(otherNames);
        }

        return this.Content is { } content && other.Content is { } otherContent && content.SameBytes(otherContent);
    }
}

/// <summary>What a derived item used: an input and its revision, or the state of an input it read first.</summary>
/// <param name="Key">The input.</param>
/// <param name="Revision">The committed revision, or 0 for a first read.</param>
/// <param name="FirstRead">The state of a first read, registered on adoption.</param>
internal readonly record struct RecordedInput(InputKey Key, long Revision, InputState? FirstRead);

/// <summary>A derived item (SPEC 23.4.5): a loaded project or a unit result, valid while its recorded inputs are current and unmarked.</summary>
internal abstract class DerivedItem
{
    private static long nextId;

    /// <summary>Gets the item identity, used by validity copies.</summary>
    public long Id { get; } = Interlocked.Increment(ref nextId);

    /// <summary>Gets or sets the recorded inputs.</summary>
    public RecordedInput[] Inputs { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether a recorded input has a newer revision.</summary>
    public bool Invalid { get; set; }

    /// <summary>Gets or sets the number of recorded inputs that are marked.</summary>
    public int Holds { get; set; }

    /// <summary>Gets or sets a value indicating whether the item is registered with its inputs.</summary>
    public bool Registered { get; set; }

    /// <summary>Gets a value indicating whether the item is valid: current and unmarked.</summary>
    public bool Valid => !this.Invalid && this.Holds == 0;
}

/// <summary>The state owner's record of one input.</summary>
internal sealed class InputEntry
{
    /// <summary>Initializes a new instance of the <see cref="InputEntry"/> class.</summary>
    /// <param name="key">The key.</param>
    public InputEntry(InputKey key)
    {
        this.Key = key;
    }

    /// <summary>Gets the key.</summary>
    public InputKey Key { get; }

    /// <summary>Gets or sets the committed revision, or 0 before the first registration.</summary>
    public long Revision { get; set; }

    /// <summary>Gets or sets the retained state.</summary>
    public InputState? State { get; set; }

    /// <summary>Gets or sets the number of the last input event.</summary>
    public long LastEvent { get; set; }

    /// <summary>Gets or sets a value indicating whether the input is marked unverified.</summary>
    public bool Marked { get; set; }

    /// <summary>Gets or sets a value indicating whether a watched-file event marked the input, so retained disk data is not reused.</summary>
    public bool Watched { get; set; }

    /// <summary>Gets the items that recorded this input.</summary>
    public List<DerivedItem> Dependents { get; } = [];
}

/// <summary>A committed view of the store that the worker reads (SPEC 23.4.6); its collections are never changed after it is taken.</summary>
/// <param name="Base">The base event number.</param>
/// <param name="Entries">The committed state of every retained input.</param>
/// <param name="Valid">The items that were valid when the comparisons were committed.</param>
internal sealed record StoreSnapshot(long Base, Dictionary<InputKey, (long Revision, InputState State)> Entries, HashSet<long> Valid);

/// <summary>The state owner's input store (SPEC 23.4.5): revisions, events, marks and the items that recorded each input.</summary>
internal sealed class InputStore
{
    private readonly Dictionary<InputKey, InputEntry> entries = new();
    private long revision;
    private long eventNumber;
    private ImmutableHashSet<InputKey> changedAfterBase = ImmutableHashSet<InputKey>.Empty;

    /// <summary>Gets the number of the latest input event.</summary>
    public long LastEvent => this.eventNumber;

    /// <summary>Gets the inputs with an event after the running check's base; replaced, never mutated, on each event.</summary>
    public ImmutableHashSet<InputKey> ChangedAfterBase => Volatile.Read(ref this.changedAfterBase);

    /// <summary>Gets the number of retained entries.</summary>
    public int Count => this.entries.Count;

    /// <summary>Gets an entry, if present.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The entry, or null.</returns>
    public InputEntry? Find(InputKey key) => this.entries.GetValueOrDefault(key);

    /// <summary>Gets or creates an entry.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The entry.</returns>
    public InputEntry Get(InputKey key)
    {
        if (!this.entries.TryGetValue(key, out var entry))
        {
            entry = new(key);
            this.entries.Add(key, entry);
        }

        return entry;
    }

    /// <summary>Records one input event: numbers it and marks each affected input, holding its dependents.</summary>
    /// <param name="checkBase">The running check's base, or -1 when no check runs.</param>
    /// <param name="watched">Whether the event is a watched-file event.</param>
    /// <param name="keys">The affected inputs.</param>
    /// <returns>The event number.</returns>
    public long Event(long checkBase, bool watched, params ReadOnlySpan<InputKey> keys)
    {
        var number = ++this.eventNumber;
        var changed = this.changedAfterBase;
        foreach (var key in keys)
        {
            var entry = this.Get(key);
            entry.LastEvent = number;
            entry.Watched |= watched;
            if (!entry.Marked)
            {
                entry.Marked = true;
                foreach (var item in entry.Dependents)
                {
                    item.Holds++;
                }
            }

            if (checkBase >= 0)
            {
                changed = changed.Add(key);
            }
        }

        Volatile.Write(ref this.changedAfterBase, changed);
        return number;
    }

    /// <summary>Starts a check: later events are published as changed after its base.</summary>
    /// <returns>The base.</returns>
    public long Begin()
    {
        Volatile.Write(ref this.changedAfterBase, ImmutableHashSet<InputKey>.Empty);
        return this.eventNumber;
    }

    /// <summary>Commits one comparison under the base rule (SPEC 23.4.5).</summary>
    /// <param name="key">The input.</param>
    /// <param name="state">The observed state.</param>
    /// <param name="checkBase">The check's base.</param>
    /// <param name="released">Receives items released from their last hold.</param>
    /// <param name="invalidated">Receives items invalidated by a new revision.</param>
    public void Commit(InputKey key, InputState state, long checkBase, List<DerivedItem> released, List<DerivedItem> invalidated)
    {
        var entry = this.Get(key);
        if (entry.LastEvent > checkBase)
        {
            return; // The mark stays for the next check.
        }

        if (entry.Revision == 0 || !state.SameIdentity(entry.State))
        {
            entry.Revision = ++this.revision;
            foreach (var item in entry.Dependents)
            {
                if (!item.Invalid)
                {
                    item.Invalid = true;
                    invalidated.Add(item);
                }
            }
        }

        entry.State = state;
        entry.Watched = false;
        if (entry.Marked)
        {
            entry.Marked = false;
            foreach (var item in entry.Dependents)
            {
                if (--item.Holds == 0 && !item.Invalid)
                {
                    released.Add(item);
                }
            }
        }
    }

    /// <summary>Registers an adopted item with its inputs, or rejects it (SPEC 23.4.6).</summary>
    /// <param name="item">The item.</param>
    /// <param name="checkBase">The check's base.</param>
    /// <returns><see langword="false"/> when a recorded input has an event after the base or a revision that is no longer current.</returns>
    public bool TryRegister(DerivedItem item, long checkBase)
    {
        foreach (var input in item.Inputs)
        {
            var entry = this.Find(input.Key);
            if (entry is not null && entry.LastEvent > checkBase)
            {
                return false;
            }

            if (input.FirstRead is { } first)
            {
                if (entry is { Revision: > 0 } && !first.SameIdentity(entry.State))
                {
                    return false;
                }
            }
            else if (entry is null || entry.Revision != input.Revision)
            {
                return false;
            }
        }

        var inputs = item.Inputs;
        var committed = inputs;
        for (var i = 0; i < inputs.Length; i++)
        {
            var entry = this.Get(inputs[i].Key);
            if (inputs[i].FirstRead is { } first && entry.Revision == 0)
            {
                entry.Revision = ++this.revision;
                entry.State = first;
            }

            if (inputs[i].FirstRead is not null)
            {
                if (ReferenceEquals(committed, inputs))
                {
                    committed = (RecordedInput[])inputs.Clone();
                }

                committed[i] = inputs[i] with { Revision = entry.Revision, FirstRead = null };
            }

            entry.Dependents.Add(item);
            if (entry.Marked)
            {
                item.Holds++;
            }
        }

        item.Inputs = committed; // The worker may still be reading the original array.
        item.Registered = true;
        return true;
    }

    /// <summary>Removes an item from its inputs.</summary>
    /// <param name="item">The item.</param>
    public void Unregister(DerivedItem item)
    {
        if (!item.Registered)
        {
            return;
        }

        foreach (var input in item.Inputs)
        {
            this.Find(input.Key)?.Dependents.Remove(item);
        }

        item.Registered = false;
    }

    /// <summary>Takes the committed view the worker reads after re-validation.</summary>
    /// <param name="checkBase">The check's base.</param>
    /// <param name="items">The current items whose validity the worker needs.</param>
    /// <returns>The snapshot.</returns>
    public StoreSnapshot Snapshot(long checkBase, IEnumerable<DerivedItem> items)
    {
        // A copy the state owner never touches again is enough, and it costs far less than persistent collections.
        var committed = new Dictionary<InputKey, (long, InputState)>(this.entries.Count);
        foreach (var (key, entry) in this.entries)
        {
            if (entry.State is { } state && entry.Revision > 0)
            {
                committed.Add(key, (entry.Revision, state));
            }
        }

        var valid = new HashSet<long>();
        foreach (var item in items)
        {
            if (item.Valid)
            {
                valid.Add(item.Id);
            }
        }

        return new(checkBase, committed, valid);
    }

    /// <summary>Lists the entries re-validation must consider.</summary>
    /// <returns>The entries with a retained state, marked, or both.</returns>
    public RevalidationTarget[] RevalidationTargets()
    {
        var count = 0;
        foreach (var entry in this.entries.Values)
        {
            if (entry.Marked || entry.State is not null)
            {
                count++;
            }
        }

        var targets = new RevalidationTarget[count];
        var index = 0;
        foreach (var (key, entry) in this.entries)
        {
            if (entry.Marked || entry.State is not null)
            {
                targets[index++] = new(key, entry.State, entry.Marked, entry.Watched);
            }
        }

        return targets;
    }

    /// <summary>Releases entries that no item records and nothing else keeps (SPEC 23.4.6).</summary>
    /// <param name="keep">Keeps an entry regardless, such as an open document.</param>
    public void Release(Func<InputKey, bool> keep)
    {
        List<InputKey>? released = null;
        foreach (var (key, entry) in this.entries)
        {
            if (entry.Dependents.Count == 0 && !entry.Marked && !keep(key))
            {
                (released ??= []).Add(key);
            }
        }

        if (released is not null)
        {
            foreach (var key in released)
            {
                this.entries.Remove(key);
            }
        }
    }

    /// <summary>Releases every retained input and dependent after the session has ended.</summary>
    internal void Clear()
    {
        this.entries.Clear();
        Volatile.Write(ref this.changedAfterBase, ImmutableHashSet<InputKey>.Empty);
    }
}
