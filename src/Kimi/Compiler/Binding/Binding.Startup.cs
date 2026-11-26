// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Startup selection vocabulary.

public enum OutputKind : byte
{
    Application,
    Library,
}

public enum StartupKind : byte
{
    None,
    Implicit,
    Explicit,
    Test,
}

/// <summary>A startup selection for the latest final Bind; this does not certify ownership or emission. <c>Symbol</c> is the explicit
/// <c>main</c>; an Implicit selection's body is the generated function, <c>MirDefKey(null, Fn, 0)</c> (DECISIONS R5-M25).</summary>
public readonly record struct StartupResult(OutputKind OutputKind, StartupKind Kind, BindingSymbol? Symbol, SourceDocument? Source, bool IsComplete);

public sealed partial class Binding
{
    private readonly List<BindingIssue> startupIssues = new();
    private readonly List<Koto> startupItems = new();

    // SPEC 22.4.5 (DECISIONS <T-M?>): the Application's platform candidates in the root walk's order, each with its own path to Platform
    // (the first in registration order, never an inherited one), and the clauses whether a platform exists rests on: each root Self
    // clause that did not bind.
    private readonly List<(StructKoto Candidate, BoundConformancePath Path)> platformCandidates = new();
    private readonly List<IsKoto> platformUnresolved = new();

    /// <summary>Gets the latest selection, invalidated by every Bind.</summary>
    public StartupResult Startup { get; private set; }

    /// <summary>Gets original runtime items in source order. Only a successful implicit selection publishes items.</summary>
    public IReadOnlyList<Koto> StartupItems => this.startupItems;

    /// <summary>Gets output-specific failures, kept separate from ordinary Binding failures.</summary>
    public IReadOnlyList<BindingIssue> StartupIssues => this.startupIssues;

    /// <summary>Selects startup after final Binding. Library functions do not receive Application signature restrictions. An Application's
    /// platform (SPEC 22.4.5) is judged in the same root walk: several platforms and a generic one are errors on every target, a platform
    /// on a row whose program supplies none and no platform on a row whose program supplies it are each the located Unsupported_Kd, and a
    /// target without a row records neither.</summary>
    /// <param name="outputKind">The requested output kind.</param>
    /// <returns>The selection and front-end startup validity, independently of later execution checks.</returns>
    public StartupResult CheckStartup(OutputKind outputKind)
    {
        if (this.Result.Mode != BindingMode.Final || this.running)
        {
            throw new InvalidOperationException("Startup selection requires a completed final Binding pass.");
        }

        if (outputKind is not (OutputKind.Application or OutputKind.Library))
        {
            throw new ArgumentOutOfRangeException(nameof(outputKind));
        }

        this.ResetStartup();
        for (var i = 1; i < this.compilation.SourceModules.Length; i++)
        {
            var dependency = this.compilation.SourceModules[i];
            FunctionKoto? ignoredMain = null;
            var ignoredCount = 0;
            SourceDocument? ignoredSource = null;
            var ignoredMultiple = false;
            this.CollectStartup(dependency.RootKoto.Members, OutputKind.Library, ref ignoredMain, ref ignoredCount, ref ignoredSource, ref ignoredMultiple);
            if (dependency.GeneratedFunction?.Body is { } dependencyBody)
            {
                this.CollectStartup(dependencyBody.Items, OutputKind.Library, ref ignoredMain, ref ignoredCount, ref ignoredSource, ref ignoredMultiple);
            }

            if (this.startupItems.Count != 0)
            {
                this.startupIssues.Add(new(this.startupItems[0], DiagnosticCode.LibraryRuntimeBody_Kd));
                this.startupItems.Clear();
            }
        }

        var root = this.compilation.Kotonoha.RootKoto;
        FunctionKoto? main = null;
        var mainCount = 0;
        SourceDocument? source = null;
        var multipleSources = false;
        this.CollectStartup(root.Members, outputKind, ref main, ref mainCount, ref source, ref multipleSources);
        if (this.compilation.Kotonoha.GeneratedFunction?.Body is { } body)
        {
            this.CollectStartup(body.Items, outputKind, ref main, ref mainCount, ref source, ref multipleSources);
        }

        // SPEC 22.4.5: an Application's root-level structs, which the root keeps among its nested containers, never among its items.
        for (var i = 0; outputKind == OutputKind.Application && i < root.NestedContainers.Count; i++)
        {
            if (root.NestedContainers[i] is StructKoto structure)
            {
                this.CollectPlatform(structure);
            }
        }

        var hasRuntime = this.startupItems.Count != 0;
        var kind = StartupKind.None;
        if (outputKind == OutputKind.Library)
        {
            if (hasRuntime)
            {
                this.startupIssues.Add(new(this.startupItems[0], DiagnosticCode.LibraryRuntimeBody_Kd));
            }
        }
        else if (hasRuntime && mainCount != 0)
        {
            this.startupIssues.Add(new(main!, DiagnosticCode.MixedStartupBodies_Kd));
        }
        else if (!hasRuntime && mainCount == 0)
        {
            this.startupIssues.Add(new(root, DiagnosticCode.MissingStartupBody_Kd));
        }
        else if (hasRuntime)
        {
            kind = StartupKind.Implicit;
        }
        else
        {
            kind = StartupKind.Explicit;
            source = main!.CodeContext.SourceDocument;
        }

        // SPEC 22 ¶1, 22.4.5: the row decides only where a platform is Unsupported; neither record reads the conformance's verdict.
        if (TargetProfile.Find(this.compilation.TargetTriple.Value) is { } row)
        {
            if (!row.ProgramPlatform)
            {
                for (var i = 0; i < this.platformCandidates.Count; i++)
                {
                    this.startupIssues.Add(new(this.platformCandidates[i].Path.Declaration, DiagnosticCode.Unsupported_Kd));
                }
            }
            else if (outputKind == OutputKind.Application && this.platformCandidates.Count == 0)
            {
                this.startupIssues.Add(new(hasRuntime ? this.startupItems[0] : main ?? (Koto)root, DiagnosticCode.Unsupported_Kd));
            }
        }

        var complete = this.Result.IsComplete && this.startupIssues.Count == 0;
        if (!complete || kind != StartupKind.Implicit)
        {
            this.startupItems.Clear();
        }

        var symbol = kind == StartupKind.Explicit ? main!.BoundSymbol : null;
        return this.Startup = new(outputKind, complete ? kind : StartupKind.None, complete ? symbol : null, complete ? source : null, complete);
    }

    /// <summary>Publishes startup diagnostics only when the caller commits to an output kind.</summary>
    public void ReportStartupDiagnostics()
    {
        this.compilation.Diagnostics.Invalidate(DiagnosticPartition.Startup);
        for (var i = 0; i < this.startupIssues.Count; i++)
        {
            var issue = this.startupIssues[i];
            if (issue.Code == DiagnosticCode.InvalidStartupMain_Kd && issue.Node is FunctionKoto { BoundSymbol.Type: null, ReturnType: { BindingState: not BindingState.Resolved } result })
            {
                // Whether main returns Unit cannot be decided when its written result Type failed.
                issue.Node.ReportDerived(DiagnosticRequirement.Startup, this.CauseKeys(result));
            }
            else if (issue.Code == DiagnosticCode.MissingStartupBody_Kd && this.compilation.Kotonoha.OmittedDeclarations is { Count: > 0 } omitted)
            {
                // The member set is incomplete where the parser skipped a declaration; whether a startup body exists rests on those Errors.
                issue.Node.ReportDerived(DiagnosticRequirement.Startup, [.. omitted]);
            }
            else if (issue.Code == DiagnosticCode.Unsupported_Kd && this.platformCandidates.Count == 0 && this.PlatformCauses() is { } causes)
            {
                // No platform (a hosted record needs a candidate): whether one exists rests on the whole root member set.
                issue.Node.ReportDerived(DiagnosticRequirement.Startup, causes);
            }
            else
            {
                // SPEC 23.3: a startup function is located at its signature, never over its whole body.
                issue.Node.Report(DiagnosticRequirement.Startup, issue.Code, span: issue.Node is FunctionKoto { SignatureSpan.Length: > 0 } function ? function.SignatureSpan : null);
            }
        }
    }

    private static bool IsRootMain(FunctionKoto function)
        => function.Name == "main" && function.Modifier.ExtractAccessibilityModifiers() == ModifierKind.Public &&
            (function.Parent is GroupKoto { IsRoot: true } || function.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } });

    private static bool ValidMain(FunctionKoto function)
        => function.Modifier == ModifierKind.Public && function.Parameters.Count == 0 && function.GenericArguments.Count == 0 &&
            function.Origins.Count == 0 && function.TypeConstraints.Count == 0 &&
            !function.IsSpecialization && !function.IsAnonymous && !function.IsGenerated && !function.IsRequirement &&
            (function.Body is not null || function.ExpressionBody is not null) && function.FindAttribute(AttributeKind.LibraryImport) is null &&
            ReferenceEquals(function.BoundSymbol?.Type, BoundType.Unit);

    private void ResetStartup()
    {
        this.Startup = default;
        this.startupItems.Clear();
        this.startupIssues.Clear();
        this.platformCandidates.Clear();
        this.platformUnresolved.Clear();
    }

    private void CollectStartup(IReadOnlyList<Koto> items, OutputKind outputKind, ref FunctionKoto? main, ref int mainCount, ref SourceDocument? source, ref bool multipleSources)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is FunctionKoto candidate)
            {
                if (TestDefinition.Marker(candidate) is not null)
                {
                    continue;
                }

                if (outputKind == OutputKind.Application && IsRootMain(candidate))
                {
                    main = candidate;
                    if (++mainCount > 1)
                    {
                        this.startupIssues.Add(new(candidate, DiagnosticCode.MultipleStartupMains_Kd));
                    }

                    if (!ValidMain(candidate))
                    {
                        this.startupIssues.Add(new(candidate, DiagnosticCode.InvalidStartupMain_Kd));
                    }
                }

                continue;
            }

            // Never inspect declaration bodies, initializer attributes, or failed compile-time selection.
            if (item is DeclarationContainerKoto or AliasKoto or CompileTimeSwitchKoto)
            {
                continue;
            }

            if (this.startupItems.Count == 0)
            {
                source = item.CodeContext.SourceDocument;
            }
            else if (outputKind == OutputKind.Application && !multipleSources && !ReferenceEquals(source, item.CodeContext.SourceDocument))
            {
                multipleSources = true;
                this.startupIssues.Add(new(item, DiagnosticCode.MultipleStartupSources_Kd));
            }

            this.startupItems.Add(item);
        }
    }

    // SPEC 22.4.5: a root struct of the Application is a platform when the registrar gave it a path of its own to Platform, by a direct or
    // a refining clause (SPEC 8.4.3); that path's clause is the hosted record's location. The two errors read the count and the struct's
    // parameters, never the conformance's verdict. A Self clause that did not bind is kept as an input the no-platform record rests on.
    private void CollectPlatform(StructKoto structure)
    {
        for (var i = 0; i < structure.ConstraintNodes.Count; i++)
        {
            var clause = structure.ConstraintNodes[i];
            if (DeclaresConformance(clause) && clause.BoundConstraint is null or { Kind: ConstraintKind.Error or ConstraintKind.Unresolved })
            {
                this.platformUnresolved.Add(clause);
            }
        }

        if (structure.BoundSymbol is not { } symbol || this.impls.Find(symbol, this.Library.GetSymbol(KimiDeclarationId.Platform)!) is not { } identity)
        {
            return;
        }

        var paths = this.impls.PathsOf(identity);
        for (var i = 0; i < paths.Count; i++)
        {
            if (paths[i].InheritedFrom is null)
            {
                this.platformCandidates.Add((structure, paths[i]));
                if (this.platformCandidates.Count > 1)
                {
                    this.startupIssues.Add(new(structure, DiagnosticCode.MultiplePlatforms_Kd));
                }

                if (structure.GenericParameterNodes.Count != 0 || structure.OriginNames.Count != 0)
                {
                    this.startupIssues.Add(new(structure, DiagnosticCode.InvalidPlatform_Kd));
                }

                return;
            }
        }
    }

    // The no-platform record's resting inputs: the parser's omitted declarations and the causes of each unresolved input the walk kept;
    // null when there is none, so the record is direct.
    private DiagnosticKey[]? PlatformCauses()
    {
        var omitted = this.compilation.Kotonoha.OmittedDeclarations;
        if (omitted is not { Count: > 0 } && this.platformUnresolved.Count == 0)
        {
            return null;
        }

        var causes = omitted is null ? new List<DiagnosticKey>() : new List<DiagnosticKey>(omitted);
        for (var i = 0; i < this.platformUnresolved.Count; i++)
        {
            causes.AddRange(this.CauseKeys(this.platformUnresolved[i]));
        }

        return [.. causes];
    }
}
