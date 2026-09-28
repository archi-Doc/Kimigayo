// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Kimi.Checking;

namespace Kimi.Lsp;

/// <summary>The inputs of one workspace check as the worker reads them (SPEC 23.4.6).</summary>
/// <remarks>
/// A committed input is read from the snapshot; an input read for the first time is read once per check and shared by
/// every unit of the check. The published set of inputs changed after the base lets a check stop early; adoption stays
/// the authority, so a stale view only wastes work.
/// The worker owns this instance; file reads are serialized before parsing.
/// </remarks>
internal sealed class CheckInputs
{
    private readonly Dictionary<InputKey, InputState> firstReads = new();
    private readonly Func<ImmutableHashSet<InputKey>> changedAfterBase;
    private readonly Dictionary<string, List<string>> openByDirectory;

    /// <summary>Initializes a new instance of the <see cref="CheckInputs"/> class.</summary>
    /// <param name="snapshot">The committed snapshot.</param>
    /// <param name="changedAfterBase">Reads the published set of inputs changed after the base.</param>
    public CheckInputs(StoreSnapshot snapshot, Func<ImmutableHashSet<InputKey>> changedAfterBase)
    {
        this.Snapshot = snapshot;
        this.changedAfterBase = changedAfterBase;
        var open = new List<string>();
        foreach (var (key, entry) in snapshot.Entries)
        {
            if (key.Kind == InputKind.File && entry.State.Overlay)
            {
                open.Add(key.Identity.Value);
            }
        }

        this.openByDirectory = GroupOpenPaths(open);
    }

    /// <summary>Gets the committed snapshot.</summary>
    public StoreSnapshot Snapshot { get; }

    /// <summary>Groups the open paths once, so each listing only visits its own directory's overlays.</summary>
    /// <param name="paths">The open file paths.</param>
    /// <returns>The paths by directory, retained for the check.</returns>
    public static Dictionary<string, List<string>> GroupOpenPaths(IEnumerable<string> paths)
    {
        var directories = new Dictionary<string, List<string>>(SourceIdentity.PathComparer);
        foreach (var path in paths)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (!directories.TryGetValue(directory, out var names))
            {
                names = [];
                directories.Add(directory, names);
            }

            names.Add(path);
        }

        return directories;
    }

    /// <summary>Gets a value indicating whether an input has an event after the base.</summary>
    /// <param name="key">The input.</param>
    /// <returns><see langword="true"/> when work that needs the input takes no effect.</returns>
    public bool IsPending(InputKey key)
    {
        var changed = this.changedAfterBase();
        return changed.Count != 0 && changed.Contains(key); // Usually nothing changed, and no path is hashed.
    }

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
        if (!this.firstReads.TryGetValue(key, out var state))
        {
            state = key.Kind == InputKind.File ? DiskReader.ReadFile(key.Identity.Value) : this.ReadListing(key, null);
            this.firstReads.Add(key, state);
        }

        return state;
    }

    /// <summary>Computes a listing: its disk names, reused or read again, merged with the open documents that match its pattern.</summary>
    /// <param name="key">The listing.</param>
    /// <param name="reuse">Retained disk names to reuse, or null to read the directory.</param>
    /// <returns>The state.</returns>
    public InputState ReadListing(InputKey key, InputState? reuse)
    {
        var directory = key.Identity.Value;
        var disk = reuse?.DiskNames is not null ? reuse : DiskReader.ReadListing(directory, key.Pattern);
        return DiskReader.MergeListing(key, disk, CollectionsMarshal.AsSpan(this.openByDirectory.GetValueOrDefault(directory)));
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
        => this.ReadFile(InputKey.File(path), path).Content!.GetBytes();

    /// <inheritdoc/>
    public override SourceContent ReadSource(string path)
    {
        var identity = SourceIdentity.FromPath(path);
        var state = this.ReadFile(InputKey.File(identity), path);
        this.sources.Add(identity);
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

    private InputState ReadFile(InputKey key, string path)
    {
        var state = this.Read(key);
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
