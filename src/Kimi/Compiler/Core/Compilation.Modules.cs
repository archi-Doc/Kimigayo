// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    private static readonly string[] RequiredAliases = ["Kimi"];
    private DependencyPartition? dependencyGraph;
    private Dictionary<Kotonoha, IReadOnlyDictionary<string, BasicValue>>? moduleVariables;
    private Dictionary<Kotonoha, Dictionary<string, Kotonoha>>? moduleReferences;
    private Dictionary<Kotonoha, string[]>? moduleAliases;
    private Dictionary<Kotonoha, ProjectFile>? moduleConfigurations;
    private Dictionary<Kotonoha, DefaultAlias[]>? defaultAliasRows;
    private string[] rootAliases = RequiredAliases;

    /// <summary>Gets the source modules by ordinal. A prepared Compilation may have none (<see cref="AnalyzesNoModule"/>): R5-M25's
    /// <c>SourceModules[0]</c> is <c>Compilation.Kotonoha</c> only when AnalyzesNoModule is false, and <c>Compilation.Check()</c>
    /// never runs on a Compilation without a source module.</summary>
    internal Kotonoha[] SourceModules { get; private set; }

    /// <summary>Gets a value indicating whether a successful preparation established no source module, the one outcome of a refusal
    /// that leaves nothing to analyze (IMPL 21.3.2: the module count's). The configuration is checked; the sources, which the resolver
    /// has read, are neither established nor parsed, and no phase runs: the check is Completed, rejected by the refusal's Error
    /// (SPEC 23.2.2), and no fact of a parsed source exists.</summary>
    internal bool AnalyzesNoModule => this.SourceModules.Length == 0;

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

    // A configured path read by the Type production (F.5; the alias target's, GroupKoto) in a configuration document of its module, as
    // KimiLibrary.CreateAbort forms its built-in syntax. The loader refused an empty path; the effective path carries no leading `::`.
    internal static Koto ParseConfiguredPath(Kotonoha module, string path)
    {
        var source = new SourceDocument($"configuration://alias/{path}", path);
        _ = module.LogicalName(source);
        var context = new CodeContext(module, sourceDocument: source);
        var tokenizer = new Tokenizer(context.DiagnosticCollection, source);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(context, ref tokenizer);
            return Parser.ParseType(ref reader, Parser.TypeContext.AnnotatedType);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    internal bool Prepare(string target, DependencyPartition graph)
    {
        this.EnsureNoBinding();
        if (this.hasParsedSource)
        {
            throw new InvalidOperationException("Create a new Compilation to change inputs after parsing source.");
        }

        // An unresolved graph's failures are its resolver's caller's records (IMPL 20.5); the root's checks read no graph and still run
        // (SPEC 23.1.2), and the graph's, which read it, are left out.
        if (graph.IsResolved && (graph.Nodes.Length == 0 || !ReferenceEquals(graph.Nodes[0].Input.Configuration, this.Project.ProjectFile)))
        {
            throw new ArgumentException("Compilation requires the resolved root configuration snapshot.", nameof(graph));
        }

        this.dependencyGraph = graph;
        return this.Prepare(target);
    }

    // The module set this compilation binds (PLAN G949 Stage 2 U3-1; the Front's U0b-6 N2), its one owner: the source modules by ordinal,
    // then its library's module (SyntaxTable.LibraryModule). Binding's readers outside a pass walk it (Binding.Modules), hover reads its
    // documents, HoldsModule tests it and ModuleOf resolves a reference in it; the program pass binds the source modules alone.
    internal (Kotonoha[] Sources, Kotonoha Library) BoundModules => (this.SourceModules, this.Library.Kotonoha);

    // The module of the set that a reference names: the library's for SyntaxTable.LibraryModule, else the source module of its ordinal.
    internal Kotonoha ModuleOf(SourceRef source)
    {
        var (sources, library) = this.BoundModules;
        return source.IsLibrary ? library : sources[source.Module];
    }

    // Whether a module is in the set. Binding.GetDocumentation documents only a declaration of such a module.
    internal bool HoldsModule(Kotonoha module)
    {
        var (sources, library) = this.BoundModules;
        return ReferenceEquals(module, library) || Array.IndexOf(sources, module) >= 0;
    }

    internal Kotonoha? FindReference(Kotonoha module, string name)
        => this.moduleReferences?.GetValueOrDefault(module)?.GetValueOrDefault(name);

    internal Dictionary<string, Kotonoha>? References(Kotonoha module)
        => this.moduleReferences?.GetValueOrDefault(module);

    internal ProjectFile? Configuration(Kotonoha module)
        => ReferenceEquals(module, this.Kotonoha) ? this.Project.ProjectFile : this.moduleConfigurations?.GetValueOrDefault(module);

    // A module's configured default aliases, each path parsed once for the Compilation (F's ruling N1-4 (1), (2)): the configuration
    // owner reads it by the Type production in a configuration document of the module, as KimiLibrary forms its built-in syntax; the
    // node lives for the Compilation and Binding resets its rows each pass. The library's row is the library's own (KimiLibrary).
    internal DefaultAlias[] DefaultAliases(Kotonoha module)
    {
        if (ReferenceEquals(module, this.Library.Kotonoha))
        {
            return this.Library.DefaultAliases;
        }

        var rows = this.defaultAliasRows ??= new();
        if (!rows.TryGetValue(module, out var parsed))
        {
            var paths = ReferenceEquals(module, this.Kotonoha) ? this.rootAliases : this.moduleAliases?.GetValueOrDefault(module) ?? RequiredAliases;
            parsed = new DefaultAlias[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                parsed[i] = new(paths[i], ParseConfiguredPath(module, paths[i]));
            }

            rows.Add(module, parsed);
        }

        return parsed;
    }

    // failed is the root's verdict. The graph's checks below read no root fact it rests on, so they run after a root failure, each
    // failure its own record (SPEC 23.1.2); the modules are established only when no check failed. A module's variables start from the
    // root's, empty after a root failure, and then stay unused.
    private bool PrepareModules(bool failed)
    {
        this.rootAliases = EffectiveAliases(this.Project.ProjectFile.Alias);
        if (this.dependencyGraph is not { } graph)
        {
            return !failed;
        }

        if (!graph.IsResolved)
        {
            return false;
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
            this.Diagnostics.InvalidateSyntax(module);
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
                if (this.TryGetSetting(node.Input.Path, name, setting, out var value))
                {
                    variables.Add(name, value);
                }
                else
                {
                    failed = true;
                }
            }

            this.moduleVariables.Add(module, variables);
        }

        // SPEC 23.1.2: each dependency setting and each supply failure of the graph is its own record, at its module's project file.
        List<(int Module, string Note)>? supplies = null;
        NativeConfiguration.ValidateGraphSupplies(graph.Nodes, ref supplies);
        for (var i = 0; i < (supplies?.Count ?? 0); i++)
        {
            var (module, note) = supplies![i];
            this.Diagnostics.Report(DiagnosticPartition.Input, DiagnosticCode.InvalidDependencyConfiguration_Kd, graph.Nodes[module].Input.Path, note: note);
            failed = true;
        }

        // IMPL A.4, 21.3.2: a module's ordinal is below SyntaxTable.LibraryModule, so a graph of more modules is refused here, before
        // any ordinal is narrowed: one Resource record at the root project file, and no source module is established (AnalyzesNoModule).
        // The module Kotonohas and per-module dictionaries built above stay, unused, until the Compilation is released.
        if (modules.Length > SyntaxTable.LibraryModule)
        {
            this.Diagnostics.Report(
                DiagnosticPartition.Input, DiagnosticCode.ModuleCountLimit_Kd, this.Project.FilePath, (long)modules.Length, (long)SyntaxTable.LibraryModule);
            modules = [];
        }

        if (failed)
        {
            return false;
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

    /// <summary>A module's configured default alias (SPEC 18.2.3, IMPL 20.2): its effective path and that path read once by the Type
    /// production in a configuration document of the module (F's ruling N1-4 (1)), the node Binding evaluates by the path walk.</summary>
    internal readonly record struct DefaultAlias(string Path, Koto Target);
}
