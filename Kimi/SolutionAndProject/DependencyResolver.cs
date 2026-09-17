// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi;

#pragma warning disable SA1402, CS1591 // Internal graph records share one vocabulary.

internal sealed record DependencySource(string LogicalPath, byte[] Bytes);

internal sealed record DependencyInput(string Path, ProjectFile Configuration, byte[] ConfigurationBytes, DependencySource[] Sources);

internal sealed class DependencyNode(string key, DependencyInput input, int parent, string referenceName)
{
    internal string Key { get; } = key;

    internal DependencyInput Input { get; } = input;

    internal int Parent { get; } = parent;

    internal string ReferenceName { get; } = referenceName;

    internal SortedDictionary<string, string> Edges { get; } = new(StringComparer.Ordinal);
}

internal sealed record DependencyPartition(DependencyNode[] Nodes, string? ReasonCode = null, string? Diagnostic = null)
{
    internal bool IsResolved => this.ReasonCode is null;
}

internal sealed record DependencyResolution(DependencyPartition Product, DependencyPartition Test);

/// <summary>Resolves exact Project graphs from one attempt's fixed local input bytes.</summary>
internal sealed class DependencyResolver
{
    private readonly Dictionary<string, DependencyInput> inputs = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string languageVersion;
    private readonly CancellationToken cancellationToken;
    private string target;
    private string? rootPath;
    private byte[]? rootConfiguration;

    private DependencyResolver(string target, string languageVersion, CancellationToken cancellationToken)
    {
        this.target = target;
        this.languageVersion = languageVersion;
        this.cancellationToken = cancellationToken;
    }

    internal static DependencyResolution Resolve(string projectPath, string target, string languageVersion, CancellationToken cancellationToken = default, byte[]? rootConfiguration = null, bool includeTests = true)
    {
        var resolver = new DependencyResolver(target, languageVersion, cancellationToken);
        if (rootConfiguration is not null)
        {
            resolver.rootPath = Path.GetFullPath(projectPath);
            resolver.rootConfiguration = rootConfiguration;
        }

        var product = resolver.ResolvePartition(projectPath, false);
        var test = !product.IsResolved ? new DependencyPartition([], "ProductUnresolved", "The product dependency partition is unresolved.") :
            includeTests ? resolver.ResolvePartition(projectPath, true) : new([], "NotRequested", "Only product dependencies were requested.");
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
            if (a.Sources[i].LogicalPath != b.Sources[i].LogicalPath || !a.Sources[i].Bytes.AsSpan().SequenceEqual(b.Sources[i].Bytes))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateAcyclic(List<DependencyNode> nodes)
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
            throw new ResolutionFailure("Cycle", $"Dependency cycle {string.Join(" -> ", names)}; reachable through {DependencyPath(nodes, current)}.");
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

    private DependencyPartition ResolvePartition(string projectPath, bool includeTests)
    {
        var nodes = new List<DependencyNode>();
        try
        {
            var root = this.Load(projectPath);
            if (this.target.Length == 0)
            {
                if (root.Configuration.Targets.Length != 1)
                {
                    throw new ResolutionFailure("TargetSelection", "Dependency resolution requires one configured target or an explicit --Target.");
                }

                this.target = root.Configuration.Targets[0];
            }

            var identities = new Dictionary<(string Id, string Version), int>();
            var paths = new HashSet<string>(this.inputs.Comparer);
            var pending = new List<(DependencyInput Input, int Index)>();
            nodes.Add(new("root", root, -1, string.Empty));
            if (root.Configuration.PackageId is { } rootId)
            {
                identities.Add((rootId, root.Configuration.PackageVersion!), 0);
            }

            pending.Add((root, 0));
            paths.Add(root.Path);
            for (var next = 0; next < pending.Count; next++)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
                var (input, index) = pending[next];
                var file = input.Configuration;
                this.ValidateEnvironment(input);
                var references = file.Dependencies.ToArray();
                Array.Sort(references, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
                AddReferences(references);
                if (includeTests && index == 0)
                {
                    if (DependencyConfiguration.ValidateReferences(file.TestDependencies) is { } failure)
                    {
                        throw new ResolutionFailure("InvalidTestDependencies", failure);
                    }

                    var tests = file.TestDependencies.ToArray();
                    Array.Sort(tests, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
                    AddReferences(tests);
                }

                void AddReferences(KeyValuePair<string, DependencyReference>[] references)
                {
                    foreach (var (name, reference) in references)
                    {
                        this.cancellationToken.ThrowIfCancellationRequested();
                        if (reference.Project is null)
                        {
                            throw new ResolutionFailure("UnsupportedPackage", $"Source Package loading remains unimplemented: {DependencyPath(nodes, index, name)}.");
                        }

                        var childPath = Path.GetFullPath(reference.Project, Path.GetDirectoryName(input.Path)!);
                        var child = this.Load(childPath);
                        if (child.Configuration.PackageId != reference.PackageId || child.Configuration.PackageVersion != reference.PackageVersion)
                        {
                            throw new ResolutionFailure("IdentityMismatch", $"Requested {reference.PackageId}@{reference.PackageVersion} at {DependencyPath(nodes, index, name)}; '{childPath}' declares {child.Configuration.PackageId}@{child.Configuration.PackageVersion}.");
                        }

                        if (this.inputs.Comparer.Equals(child.Path, root.Path))
                        {
                            throw new ResolutionFailure("Cycle", $"Dependency cycle returns to the root: {DependencyPath(nodes, index, name)}.");
                        }

                        if (child.Configuration.OutputKind != OutputKind.Library)
                        {
                            throw new ResolutionFailure("NotLibrary", $"Dependency {DependencyPath(nodes, index, name)} must name a Library Project: '{childPath}'.");
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
                            throw new ResolutionFailure("ConflictingInput", $"Conflicting inputs for {reference.PackageId}@{reference.PackageVersion}: {DependencyPath(nodes, childIndex)} ('{nodes[childIndex].Input.Path}') and {DependencyPath(nodes, index, name)} ('{childPath}').");
                        }

                        var key = nodes[childIndex].Key;
                        if (nodes[index].Edges.TryGetValue(name, out var previous) && previous != key)
                        {
                            throw new ResolutionFailure("ReferenceReassignment", $"Test dependencies cannot reassign product reference '{name}'.");
                        }

                        nodes[index].Edges[name] = key;
                        if (paths.Add(child.Path))
                        {
                            pending.Add((child, childIndex));
                        }
                    }
                }
            }

            ValidateAcyclic(nodes);
            return new(nodes.ToArray());
        }
        catch (ResolutionFailure ex)
        {
            return new([], ex.Code, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or TinyhandException)
        {
            return new([], "InvalidProjectInput", ex.Message);
        }
    }

    private DependencyInput Load(string path)
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (this.inputs.TryGetValue(path, out var input))
        {
            return input;
        }

        var bytes = this.rootConfiguration is not null && this.inputs.Comparer.Equals(path, this.rootPath) ? this.rootConfiguration : File.ReadAllBytes(path);
        var file = ProjectFile.Load(bytes) ?? throw new ResolutionFailure("InvalidConfiguration", $"Empty Project configuration: '{path}'.");
        var directory = Path.GetDirectoryName(path)!;
        var tests = new HashSet<string>(this.inputs.Comparer);
        foreach (var source in file.TestSources)
        {
            if (!tests.Add(Path.GetFullPath(source, directory)))
            {
                throw new ResolutionFailure("DuplicateTestSource", $"Duplicate resolved TestSources path in '{path}': {source}.");
            }
        }

        var paths = Directory.GetFiles(directory, "*.kimi", SearchOption.TopDirectoryOnly);
        Array.Sort(paths, StringComparer.Ordinal);
        var sources = new List<DependencySource>(paths.Length);
        foreach (var source in paths)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            if (!tests.Contains(source))
            {
                sources.Add(new(Path.GetFileName(source), File.ReadAllBytes(source)));
            }
        }

        input = new(path, file, bytes, sources.ToArray());
        this.inputs.Add(path, input);
        return input;
    }

    private void ValidateEnvironment(DependencyInput input)
    {
        if (!input.Configuration.Targets.Contains(this.target, StringComparer.Ordinal))
        {
            throw new ResolutionFailure("TargetMismatch", $"Project '{input.Path}' does not allow target '{this.target}'.");
        }

        if ((input.Configuration.LangVersion ?? this.languageVersion) != this.languageVersion)
        {
            throw new ResolutionFailure("LanguageMismatch", $"Project '{input.Path}' requires a different language version.");
        }
    }

    private sealed class ResolutionFailure(string code, string message) : Exception(message)
    {
        internal string Code { get; } = code;
    }
}
