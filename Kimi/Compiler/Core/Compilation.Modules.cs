// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public partial class Compilation
{
    private static readonly string[] RequiredAliases = ["Kimi"];
    private DependencyPartition? dependencyGraph;
    private Dictionary<Kotonoha, IReadOnlyDictionary<string, BasicValue>>? moduleVariables;
    private Dictionary<Kotonoha, Dictionary<string, Kotonoha>>? moduleReferences;
    private Dictionary<Kotonoha, string[]>? moduleAliases;
    private Dictionary<Kotonoha, ProjectFile>? moduleConfigurations;
    private string[] rootAliases = RequiredAliases;

    internal Kotonoha[] SourceModules { get; private set; }

    internal static string[] EffectiveAliases(string[] additions)
    {
        if (additions.Length == 0)
        {
            return RequiredAliases;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal) { "Kimi" };
        foreach (var path in additions)
        {
            paths.Add(path.StartsWith("::", StringComparison.Ordinal) ? path[2..] : path);
        }

        return paths.Count == 1 ? RequiredAliases : paths.Order(StringComparer.Ordinal).ToArray();
    }

    internal bool Prepare(string target, DependencyPartition graph)
    {
        if (this.hasParsedSource)
        {
            throw new InvalidOperationException("Create a new Compilation to change inputs after parsing source.");
        }

        if (!graph.IsResolved || graph.Nodes.Length == 0 || !ReferenceEquals(graph.Nodes[0].Input.Configuration, this.Project.ProjectFile))
        {
            throw new ArgumentException("Compilation requires the resolved root configuration snapshot.", nameof(graph));
        }

        this.dependencyGraph = graph;
        return this.Prepare(target);
    }

    internal Kotonoha? FindReference(Kotonoha module, string name)
        => this.moduleReferences?.GetValueOrDefault(module)?.GetValueOrDefault(name);

    internal Dictionary<string, Kotonoha>? References(Kotonoha module)
        => this.moduleReferences?.GetValueOrDefault(module);

    internal ProjectFile? Configuration(Kotonoha module)
        => ReferenceEquals(module, this.Kotonoha) ? this.Project.ProjectFile : this.moduleConfigurations?.GetValueOrDefault(module);

    internal string[] DefaultAliases(Kotonoha module)
        => ReferenceEquals(module, this.Kotonoha) ? this.rootAliases : this.moduleAliases?.GetValueOrDefault(module) ?? RequiredAliases;

    private bool PrepareModules()
    {
        this.rootAliases = EffectiveAliases(this.Project.ProjectFile.Alias);
        if (this.dependencyGraph is not { } graph)
        {
            return true;
        }

        var modules = new Kotonoha[graph.Nodes.Length];
        var identities = new Dictionary<string, Kotonoha>(modules.Length, StringComparer.Ordinal);
        this.moduleReferences = new(modules.Length);
        this.moduleVariables = new(modules.Length);
        this.moduleAliases = new(modules.Length);
        this.moduleConfigurations = new(modules.Length);
        for (var i = 0; i < modules.Length; i++)
        {
            var node = graph.Nodes[i];
            var module = i == 0 ? this.Kotonoha : new Kotonoha(this, node.Key, node.Input.Path);
            module.DiagnosticCollection.ClearDiagnostic();
            modules[i] = module;
            identities.Add(node.Key, module);
            this.moduleAliases.Add(module, EffectiveAliases(node.Input.Configuration.Alias));
            this.moduleConfigurations.Add(module, node.Input.Configuration);
            if (i == 0)
            {
                continue;
            }

            var variables = new Dictionary<string, BasicValue>(this.Variables, StringComparer.Ordinal);
            foreach (var name in this.Project.ProjectFile.CompileTimeSettings.Keys)
            {
                variables.Remove(name);
            }

            foreach (var (name, setting) in node.Input.Configuration.CompileTimeSettings)
            {
                if (!this.TryGetIdentifier(name, out _) || !TokenHelper.GetKeywordOrIdentifierKind(name).IsIdentifierOrContextualKeyword() ||
                    variables.ContainsKey(name) || setting is null || !setting.TryGetValue(out var value))
                {
                    module.DiagnosticCollection.Add(default, DiagnosticCode.InvalidCompileTimeSetting_Kd, name);
                    return false;
                }

                variables.Add(name, value);
            }

            this.moduleVariables.Add(module, variables);
        }

        for (var i = 0; i < modules.Length; i++)
        {
            var references = new Dictionary<string, Kotonoha>(graph.Nodes[i].Edges.Count, StringComparer.Ordinal);
            foreach (var (name, key) in graph.Nodes[i].Edges)
            {
                references.Add(name, identities[key]);
            }

            this.moduleReferences.Add(modules[i], references);
        }

        this.SourceModules = modules;
        return true;
    }
}
