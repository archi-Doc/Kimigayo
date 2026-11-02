// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;
using Kimi.Compiler;

namespace Kimi;

#pragma warning disable SA1402, CS1591 // Internal graph records share one vocabulary.

internal sealed record DependencySource(string LogicalPath, SourceContent Content);

internal sealed record DependencyInput(string Path, ProjectFile Configuration, byte[] ConfigurationBytes, DependencySource[] Sources);

internal sealed class DependencyNode(string key, DependencyInput input, int parent, string referenceName)
{
    internal string Key { get; } = key;

    internal DependencyInput Input { get; } = input;

    internal int Parent { get; } = parent;

    internal string ReferenceName { get; } = referenceName;

    internal SortedDictionary<string, string> Edges { get; } = new(StringComparer.Ordinal);
}

/// <summary>One partition: its nodes, or its independent failures (SPEC 23.1.2) when it is unresolved.</summary>
internal sealed record DependencyPartition(DependencyNode[] Nodes, ResolutionFailure[]? Failures = null)
{
    internal bool IsResolved => this.Failures is null;
}

/// <summary>One failure of a resolution: the reason code a lock records and its note.</summary>
internal readonly record struct ResolutionFailure(string Code, string Message);

internal sealed record DependencyResolution(DependencyPartition Product, DependencyPartition Test);

/// <summary>Resolves exact Project graphs from one attempt's fixed local input bytes.</summary>
internal sealed class DependencyResolver
{
    private readonly Dictionary<string, DependencyInput?> inputs = new(SourceIdentity.PathComparer); // null: the load failed
    private readonly string languageVersion;
    private readonly CancellationToken cancellationToken;
    private readonly CheckInputSource source;
    private string target;
    private string? rootPath;
    private byte[]? rootConfiguration;

    private DependencyResolver(string target, string languageVersion, CancellationToken cancellationToken, CheckInputSource source)
    {
        this.target = target;
        this.languageVersion = languageVersion;
        this.cancellationToken = cancellationToken;
        this.source = source;
    }

    internal static DependencyResolution Resolve(string projectPath, string target, string languageVersion, CancellationToken cancellationToken = default, byte[]? rootConfiguration = null, bool includeTests = true, CheckInputSource? source = null)
    {
        var resolver = new DependencyResolver(target, languageVersion, cancellationToken, source ?? CheckInputSource.Disk);
        if (rootConfiguration is not null)
        {
            resolver.rootPath = Path.GetFullPath(projectPath);
            resolver.rootConfiguration = rootConfiguration;
        }

        var product = resolver.ResolvePartition(projectPath, false);
        var test = !product.IsResolved ? new DependencyPartition([], [new("ProductUnresolved", "The product dependency partition is unresolved.")]) :
            includeTests ? resolver.ResolvePartition(projectPath, true) : new([], [new("NotRequested", "Only product dependencies were requested.")]);
        return new(product, test);
    }

    private static bool SameInput(DependencyInput a, DependencyInput b, string languageVersion)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        var x = a.Configuration;
        var y = b.Configuration;
        if ((x.LangVersion ?? languageVersion) != (y.LangVersion ?? languageVersion) || x.OutputKind != y.OutputKind ||
            x.CompileTimeSettings.Count != y.CompileTimeSettings.Count || x.Dependencies.Count != y.Dependencies.Count || a.Sources.Length != b.Sources.Length ||
            !new HashSet<string>(Compiler.Compilation.EffectiveAliases(x.Alias), StringComparer.Ordinal).SetEquals(Compiler.Compilation.EffectiveAliases(y.Alias)))
        {
            return false;
        }

        foreach (var (name, value) in x.CompileTimeSettings)
        {
            if (!y.CompileTimeSettings.TryGetValue(name, out var other) || value != other)
            {
                return false;
            }
        }

        foreach (var (name, reference) in x.Dependencies)
        {
            if (!y.Dependencies.TryGetValue(name, out var other) || reference.PackageId != other.PackageId || reference.PackageVersion != other.PackageVersion || (reference.Project is null) != (other.Project is null))
            {
                return false;
            }
        }

        for (var i = 0; i < a.Sources.Length; i++)
        {
            if (a.Sources[i].LogicalPath != b.Sources[i].LogicalPath || !a.Sources[i].Content.SameBytes(b.Sources[i].Content))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateAcyclic(List<DependencyNode> nodes, List<ResolutionFailure> failures)
    {
        var indices = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
        var incoming = new int[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            indices.Add(nodes[i].Key, i);
        }

        foreach (var node in nodes)
        {
            foreach (var key in node.Edges.Values)
            {
                incoming[indices[key]]++;
            }
        }

        var ready = new Queue<int>();
        for (var i = 0; i < incoming.Length; i++)
        {
            if (incoming[i] == 0)
            {
                ready.Enqueue(i);
            }
        }

        var visited = 0;
        while (ready.TryDequeue(out var index))
        {
            visited++;
            foreach (var key in nodes[index].Edges.Values)
            {
                var child = indices[key];
                if (--incoming[child] == 0)
                {
                    ready.Enqueue(child);
                }
            }
        }

        if (visited != nodes.Count)
        {
            // Every residual node has a residual predecessor. Following one per node
            // finds a closed cycle, including when the first residual node is downstream.
            var predecessor = new int[nodes.Count];
            for (var i = 0; i < nodes.Count; i++)
            {
                if (incoming[i] != 0)
                {
                    foreach (var key in nodes[i].Edges.Values)
                    {
                        var child = indices[key];
                        if (incoming[child] != 0)
                        {
                            predecessor[child] = i;
                        }
                    }
                }
            }

            var current = Array.FindIndex(incoming, static x => x != 0);
            var positions = new Dictionary<int, int>();
            var chain = new List<int>();
            while (positions.TryAdd(current, chain.Count))
            {
                chain.Add(current);
                current = predecessor[current];
            }

            var names = new List<string>();
            for (var i = chain.Count - 1; i >= positions[current]; i--)
            {
                names.Add(nodes[chain[i]].Key);
            }

            names.Add(names[0]);
            failures.Add(new("Cycle", $"Dependency cycle {string.Join(" -> ", names)}; reachable through {DependencyPath(nodes, current)}."));
        }
    }

    private static string DependencyPath(List<DependencyNode> nodes, int index, string? last = null)
    {
        var names = new List<string>();
        if (last is not null)
        {
            names.Add(last);
        }

        while (index > 0)
        {
            names.Add(nodes[index].ReferenceName);
            index = nodes[index].Parent;
        }

        names.Add("root");
        names.Reverse();
        return string.Join(" -> ", names);
    }

    private static KeyValuePair<string, DependencyReference>[] Sorted(IEnumerable<KeyValuePair<string, DependencyReference>> references)
    {
        var sorted = references.ToArray();
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return sorted;
    }

    // The root's test references belong to the test partition, which validates them here; an invalid one is recorded, not resolved.
    private static List<KeyValuePair<string, DependencyReference>> ValidTestReferences(
        Dictionary<string, DependencyReference> references, List<ResolutionFailure> failures)
    {
        var valid = new List<KeyValuePair<string, DependencyReference>>(references.Count);
        foreach (var pair in references)
        {
            List<string>? invalid = null;
            DependencyConfiguration.ValidateReference(pair.Key, pair.Value, ref invalid);
            if (invalid is null)
            {
                valid.Add(pair);
                continue;
            }

            foreach (var note in invalid)
            {
                failures.Add(new("InvalidTestDependencies", note));
            }
        }

        return valid;
    }

    // SPEC 23.1.2: each independent failure of the graph is recorded. A node that fails to load adds no children, a failed reference
    // adds no edge, and without the root or its target nothing else resolves: those dependents are left out.
    private DependencyPartition ResolvePartition(string projectPath, bool includeTests)
    {
        var failures = new List<ResolutionFailure>();
        if (this.Load(projectPath, failures) is not { } root)
        {
            return new([], [.. failures]);
        }

        if (this.target.Length == 0)
        {
            if (root.Configuration.Targets.Length != 1)
            {
                return new([], [new("TargetSelection", "Dependency resolution requires one configured target or an explicit --Target.")]);
            }

            this.target = root.Configuration.Targets[0];
        }

        var identities = new Dictionary<(string Id, string Version), int>();
        var paths = new HashSet<string>(this.inputs.Comparer) { root.Path };
        var pending = new List<(DependencyInput Input, int Index)> { (root, 0) };
        var nodes = new List<DependencyNode> { new("root", root, -1, string.Empty) };
        if (root.Configuration.PackageId is { } rootId)
        {
            identities.Add((rootId, root.Configuration.PackageVersion!), 0);
        }

        for (var next = 0; next < pending.Count; next++)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            var (input, index) = pending[next];
            this.ValidateEnvironment(input, failures);
            AddReferences(Sorted(input.Configuration.Dependencies));
            if (includeTests && index == 0)
            {
                AddReferences(Sorted(ValidTestReferences(input.Configuration.TestDependencies, failures)));
            }

            void AddReferences(KeyValuePair<string, DependencyReference>[] references)
            {
                foreach (var (name, reference) in references)
                {
                    this.cancellationToken.ThrowIfCancellationRequested();
                    if (this.Reference(nodes, input, index, name, reference, failures) is not { } child)
                    {
                        continue;
                    }

                    var identity = (reference.PackageId, reference.PackageVersion);
                    if (!identities.TryGetValue(identity, out var childIndex))
                    {
                        childIndex = nodes.Count;
                        identities.Add(identity, childIndex);
                        nodes.Add(new(reference.PackageId + "@" + reference.PackageVersion, child, index, name));
                    }
                    else if (!SameInput(nodes[childIndex].Input, child, this.languageVersion))
                    {
                        var message = $"Conflicting inputs for {reference.PackageId}@{reference.PackageVersion}: " +
                            $"{DependencyPath(nodes, childIndex)} ('{nodes[childIndex].Input.Path}') and " +
                            $"{DependencyPath(nodes, index, name)} ('{child.Path}').";
                        failures.Add(new("ConflictingInput", message));
                        continue;
                    }

                    var key = nodes[childIndex].Key;
                    if (nodes[index].Edges.TryGetValue(name, out var previous) && previous != key)
                    {
                        failures.Add(new("ReferenceReassignment", $"Test dependencies cannot reassign product reference '{name}'."));
                        continue;
                    }

                    nodes[index].Edges[name] = key;
                    if (paths.Add(child.Path))
                    {
                        pending.Add((child, childIndex));
                    }
                }
            }
        }

        ValidateAcyclic(nodes, failures);
        return failures.Count == 0 ? new(nodes.ToArray()) : new([], [.. failures]);
    }

    // Loads one reference's project; a failure of the reference is recorded and returns null, so no edge is added.
    private DependencyInput? Reference(
        List<DependencyNode> nodes, DependencyInput input, int index, string name, DependencyReference reference, List<ResolutionFailure> failures)
    {
        if (reference.Project is null)
        {
            failures.Add(new("UnsupportedPackage", $"Source Package loading remains unimplemented: {DependencyPath(nodes, index, name)}."));
            return null;
        }

        if (this.Load(Path.Combine(Path.GetDirectoryName(input.Path)!, reference.Project), failures) is not { } child)
        {
            return null;
        }

        if (child.Configuration.PackageId != reference.PackageId || child.Configuration.PackageVersion != reference.PackageVersion)
        {
            var message = $"Requested {reference.PackageId}@{reference.PackageVersion} at {DependencyPath(nodes, index, name)}; " +
                $"'{child.Path}' declares {child.Configuration.PackageId}@{child.Configuration.PackageVersion}.";
            failures.Add(new("IdentityMismatch", message));
            return null;
        }

        if (this.inputs.Comparer.Equals(child.Path, nodes[0].Input.Path))
        {
            failures.Add(new("Cycle", $"Dependency cycle returns to the root: {DependencyPath(nodes, index, name)}."));
            return null;
        }

        if (child.Configuration.OutputKind != OutputKind.Library)
        {
            failures.Add(new("NotLibrary", $"Dependency {DependencyPath(nodes, index, name)} must name a Library Project: '{child.Path}'."));
            return null;
        }

        return child;
    }

    // Loads one project; each failure is recorded and the node is not created (its children depend on it). A failed load is
    // remembered, so a project that several references reach is reported once (SPEC 23.1.2).
    private DependencyInput? Load(string path, List<ResolutionFailure> failures)
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        try
        {
            path = Path.GetFullPath(path);
            if (this.inputs.TryGetValue(path, out var input))
            {
                return input;
            }

            var bytes = this.rootConfiguration is not null && this.inputs.Comparer.Equals(path, this.rootPath)
                ? this.rootConfiguration : this.source.ReadAllBytes(path);
            if (ProjectFile.Load(bytes) is not { } file)
            {
                failures.Add(new("InvalidConfiguration", $"Empty Project configuration: '{path}'."));
                return this.inputs[path] = null;
            }

            var directory = Path.GetDirectoryName(path)!;
            if (Invalid(path, DependencyConfiguration.Validate(file, directory, out var tests), failures))
            {
                return this.inputs[path] = null;
            }

            var listed = this.source.GetFiles(directory, "*.kimi");
            var sources = new List<DependencySource>(listed.Length);
            foreach (var source in listed)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
                if (tests?.Contains(source) != true)
                {
                    sources.Add(new(Path.GetFileName(source), this.source.ReadSource(source)));
                }
            }

            input = new(path, file, bytes, sources.ToArray());
            this.inputs.Add(path, input);
            return input;
        }
        catch (ProjectConfigurationException ex)
        {
            Invalid(path, ex.Failures, failures);
            return this.inputs[path] = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TinyhandException)
        {
            failures.Add(new("InvalidProjectInput", ex.Message));
            return this.inputs[path] = null;
        }

        static bool Invalid(string path, IReadOnlyList<string> notes, List<ResolutionFailure> failures)
        {
            foreach (var note in notes)
            {
                failures.Add(new("InvalidConfiguration", $"Invalid project configuration '{path}': {note}"));
            }

            return notes.Count != 0;
        }
    }

    private void ValidateEnvironment(DependencyInput input, List<ResolutionFailure> failures)
    {
        if (!input.Configuration.Targets.Contains(this.target, StringComparer.Ordinal))
        {
            failures.Add(new("TargetMismatch", $"Project '{input.Path}' does not allow target '{this.target}'."));
        }

        if ((input.Configuration.LangVersion ?? this.languageVersion) != this.languageVersion)
        {
            failures.Add(new("LanguageMismatch", $"Project '{input.Path}' requires a different language version."));
        }
    }
}
