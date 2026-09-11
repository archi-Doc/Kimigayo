// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

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
}

/// <summary>A startup selection for the latest final Bind; this does not certify ownership or emission.</summary>
public readonly record struct StartupResult(OutputKind OutputKind, StartupKind Kind, FunctionKoto? Function, SourceDocument? Source, bool IsComplete);

public sealed partial class Binding
{
    private readonly List<BindingIssue> startupIssues = new();
    private readonly List<Koto> startupItems = new();

    /// <summary>Gets the latest selection, invalidated by every Bind.</summary>
    public StartupResult Startup { get; private set; }

    /// <summary>Gets original runtime items in source order. Only a successful implicit selection publishes items.</summary>
    public IReadOnlyList<Koto> StartupItems => this.startupItems;

    /// <summary>Gets output-specific failures, kept separate from ordinary Binding failures.</summary>
    public IReadOnlyList<BindingIssue> StartupIssues => this.startupIssues;

    /// <summary>Selects startup after final Binding. Library functions do not receive Application signature restrictions.</summary>
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

        var hasRuntime = this.startupItems.Count != 0;
        var kind = StartupKind.None;
        FunctionKoto? function = null;
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
            function = this.compilation.Kotonoha.GeneratedFunction;
        }
        else
        {
            kind = StartupKind.Explicit;
            function = main;
            source = main!.CodeContext.SourceDocument;
        }

        var complete = this.Result.IsComplete && this.startupIssues.Count == 0;
        if (!complete || kind != StartupKind.Implicit)
        {
            this.startupItems.Clear();
        }

        return this.Startup = new(outputKind, complete ? kind : StartupKind.None, complete ? function : null, complete ? source : null, complete);
    }

    /// <summary>Publishes startup diagnostics only when the caller commits to an output kind.</summary>
    public void ReportStartupDiagnostics()
    {
        for (var i = 0; i < this.startupIssues.Count; i++)
        {
            var issue = this.startupIssues[i];
            issue.Node.AddDiagnostic(issue.Code);
        }
    }

    private static bool IsRootMain(FunctionKoto function)
        => function.Name == "main" && function.Modifier.ExtractAccessibilityModifiers() == ModifierKind.Public &&
            (function.Parent is GroupKoto { IsRoot: true } || function.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } });

    private static bool ValidMain(FunctionKoto function)
        => function.Modifier == ModifierKind.Public && function.Parameters.Count == 0 && function.GenericArguments.Count == 0 &&
            function.Origins.Count == 0 && function.TypeConstraints.Count == 0 &&
            !function.IsSpecialization && !function.IsAnonymous && !function.IsGenerated && !function.IsRequirement &&
            (function.Body is not null || function.ExpressionBody is not null) && !Parser.HasLibraryImport(function.AttributeChain) &&
            ReferenceEquals(function.BoundSymbol?.Type, BoundType.Unit);

    private void ResetStartup()
    {
        this.Startup = default;
        this.startupItems.Clear();
        this.startupIssues.Clear();
    }

    private void CollectStartup(IReadOnlyList<Koto> items, OutputKind outputKind, ref FunctionKoto? main, ref int mainCount, ref SourceDocument? source, ref bool multipleSources)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is FunctionKoto candidate)
            {
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
            if (item is DeclarationContainerKoto or AliasKoto or CompileTimeMatchKoto)
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
}
