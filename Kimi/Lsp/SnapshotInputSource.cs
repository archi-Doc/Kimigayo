// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Kimi.Checking;

namespace Kimi.Lsp;

/// <summary>The inputs of one workspace check as the worker reads them (SPEC 23.4.6).</summary>
/// <remarks>
/// A committed input is read from the snapshot; an input read for the first time is read once per check and shared by
/// every unit of the check. The published set of inputs changed after the base lets a check stop early; adoption stays
/// the authority, so a stale view only wastes work.
/// </remarks>
internal sealed class CheckInputs
{
    private readonly ConcurrentDictionary<InputKey, InputState> firstReads = new();
    private readonly Func<ImmutableHashSet<InputKey>> changedAfterBase;
    private readonly ILookup<string, string> openByDirectory;

    /// <summary>Initializes a new instance of the <see cref="CheckInputs"/> class.</summary>
    /// <param name="snapshot">The committed snapshot.</param>
    /// <param name="changedAfterBase">Reads the published set of inputs changed after the base.</param>
    public CheckInputs(StoreSnapshot snapshot, Func<ImmutableHashSet<InputKey>> changedAfterBase)
    {
        this.Snapshot = snapshot;
        this.changedAfterBase = changedAfterBase;
        this.openByDirectory = snapshot.Entries
            .Where(static x => x.Key.Kind == InputKind.File && x.Value.State.Overlay)
            .ToLookup(static x => Path.GetDirectoryName(x.Key.Identity.Value) ?? string.Empty, static x => x.Key.Identity.Value, SourceIdentity.PathComparer);
    }

    /// <summary>Gets the committed snapshot.</summary>
    public StoreSnapshot Snapshot { get; }

    /// <summary>Gets a value indicating whether an input has an event after the base.</summary>
    /// <param name="key">The input.</param>
    /// <returns><see langword="true"/> when work that needs the input takes no effect.</returns>
    public bool IsPending(InputKey key) => this.changedAfterBase().Contains(key);

    /// <summary>Reads an input from the snapshot, or reads it for the first time.</summary>
    /// <param name="key">The input.</param>
    /// <param name="revision">The committed revision, or 0 for a first read.</param>
    /// <returns>The state.</returns>
    /// <exception cref="PendingInputException">The input has an event after the base.</exception>
    public InputState Read(InputKey key, out long revision)
    {
        if (this.IsPending(key))
        {
            throw new PendingInputException(key.ToString());
        }

        if (this.Snapshot.Entries.TryGetValue(key, out var entry))
        {
            revision = entry.Revision;
            return entry.State;
        }

        revision = 0;
        return this.firstReads.GetOrAdd(key, static (key, self) => key.Kind == InputKind.File ? DiskReader.ReadFile(key.Identity.Value) : self.ReadListing(key, null), this);
    }

    /// <summary>Computes a listing: its disk names, reused or read again, merged with the open documents that match its pattern.</summary>
    /// <param name="key">The listing.</param>
    /// <param name="reuse">Retained disk names to reuse, or null to read the directory.</param>
    /// <returns>The state.</returns>
    public InputState ReadListing(InputKey key, InputState? reuse)
    {
        var directory = key.Identity.Value;
        var disk = reuse?.DiskNames is { } names ? reuse : DiskReader.ReadListing(directory, key.Pattern);
        if (!disk.Established)
        {
            return disk;
        }

        var extension = key.Pattern[1..];
        var merged = new SortedSet<string>(disk.DiskNames!, StringComparer.Ordinal);
        foreach (var path in this.openByDirectory[directory])
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                merged.Add(path);
            }
        }

        return new() { Names = merged.ToArray(), DiskNames = disk.DiskNames, Stamp = disk.Stamp };
    }
}

/// <summary>A <see cref="CheckInputSource"/> over one check's inputs that records what one derived item reads.</summary>
internal sealed class SnapshotInputSource : CheckInputSource
{
    private readonly CheckInputs inputs;
    private readonly Dictionary<InputKey, RecordedInput> recorded = new();
    private readonly List<SourceIdentity> sources = [];

    /// <summary>Initializes a new instance of the <see cref="SnapshotInputSource"/> class.</summary>
    /// <param name="inputs">The check's inputs.</param>
    public SnapshotInputSource(CheckInputs inputs)
    {
        this.inputs = inputs;
    }

    /// <summary>Gets the source files read through <see cref="ReadSource"/>.</summary>
    public IReadOnlyList<SourceIdentity> Sources => this.sources;

    /// <summary>Gets a value indicating whether a read was rejected by the base rule, even if its exception was caught.</summary>
    public bool HasPendingInput { get; private set; }

    /// <summary>Gets the recorded inputs.</summary>
    /// <param name="additional">Inputs recorded by another item that this item also relies on.</param>
    /// <returns>The recorded inputs.</returns>
    public RecordedInput[] GetRecorded(RecordedInput[]? additional = null)
    {
        if (additional is not null)
        {
            foreach (var input in additional)
            {
                this.recorded.TryAdd(input.Key, input);
            }
        }

        return this.recorded.Values.ToArray();
    }

    /// <inheritdoc/>
    public override byte[] ReadAllBytes(string path)
        => this.ReadFile(path).Content!.GetBytes();

    /// <inheritdoc/>
    public override SourceContent ReadSource(string path)
    {
        var state = this.ReadFile(path);
        this.sources.Add(SourceIdentity.FromPath(path));
        return state.Content!;
    }

    /// <inheritdoc/>
    public override string[] GetFiles(string directory, string pattern)
    {
        var state = this.Read(InputKey.Listing(directory, pattern));
        if (!state.Established)
        {
            throw Unreadable(state);
        }

        return state.Names!;
    }

    private static IOException Unreadable(InputState state)
        => state.Failure == DesynchronizedInputException.Failure ? new DesynchronizedInputException() : new IOException(state.Failure);

    private InputState ReadFile(string path)
    {
        var state = this.Read(InputKey.File(path));
        if (!state.Established)
        {
            throw Unreadable(state);
        }

        if (state.Absent)
        {
            throw new FileNotFoundException("Could not find file '" + path + "'.", path);
        }

        return state;
    }

    private InputState Read(InputKey key)
    {
        try
        {
            var state = this.inputs.Read(key, out var revision);
            this.recorded.TryAdd(key, new(key, revision, revision == 0 ? state : null));
            return state;
        }
        catch (PendingInputException)
        {
            this.HasPendingInput = true;
            throw;
        }
    }
}
