// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns one compilation's analysis, file batches and complete baseline reconciliation.
// Input: Roslyn compilation callbacks and validated configuration. Output: diagnostics and a complete Report marker.
// Each file publishes observations once; completion sorts and merges once after all callbacks finish.
// Must not share mutable compilation state, perform file I/O or issue a completion marker after invalid input/cancellation.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kimi.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class CodeRulesAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => CodeRule.Supported;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext start)
    {
        var errors = new List<Diagnostic>();
        var config = CodeRulesConfiguration.Read(start.Options, start.CancellationToken, errors);
        var baseline = config is null ? null : CodeRulesBaseline.Read(config.Baseline, config.BaselineFile, errors);
        var batches = new List<List<CodeRuleObservation>>();
        var trees = 0;
        start.RegisterSyntaxTreeAction(context =>
        {
            if (config is null || errors.Count != 0)
            {
                return;
            }

            var path = config.Relative(context.Tree.FilePath);
            if (path is null || !path.StartsWith("src/Kimi/", StringComparison.Ordinal))
            {
                return;
            }

            var source = new CodeRulesSource(start.Compilation, context.Tree, path, context.CancellationToken);
            foreach (var node in source.Root.DescendantNodes(descendIntoTrivia: true))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (node is AttributeSyntax attribute)
                {
                    CheckSuppression(source, attribute, context.ReportDiagnostic, context.CancellationToken);
                }
                else if (node is PragmaWarningDirectiveTriviaSyntax pragma)
                {
                    CheckSuppression(pragma, context.ReportDiagnostic);
                }
            }

            if (source.Observations is { Count: > 0 } observations)
            {
                lock (batches)
                {
                    batches.Add(observations);
                }
            }

            Interlocked.Increment(ref trees);
        });
        start.RegisterCompilationEndAction(context =>
        {
            foreach (var error in errors)
            {
                context.ReportDiagnostic(error);
            }

            if (config is null || baseline is null || errors.Count != 0)
            {
                return;
            }

            var observations = new List<CodeRuleObservation>(batches.Sum(x => x.Count));
            foreach (var batch in batches)
            {
                observations.AddRange(batch);
            }

            batches.Clear();
            baseline.Complete(observations, config.Mode, context.ReportDiagnostic, context.CancellationToken);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (config.Mode == "Report")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CodeRule.Complete,
                    Location.None,
                    config.Checksums.Add("treeCount", trees.ToString(CultureInfo.InvariantCulture))));
            }
        });
    }
}
