// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns one compilation's analysis, file batches and complete baseline reconciliation.
// Input: Roslyn compilation callbacks and validated configuration. Output: diagnostics and, in Verify and Report,
// the completion marker that proves the analysis ran on the checksummed policy.
// Each file publishes observations once; completion sorts and merges once after all callbacks finish.
// Must not share mutable compilation state, perform file I/O or issue a completion marker after invalid input/cancellation.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
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
        var notices = new List<Diagnostic>();
        var architecture = config is null || errors.Count != 0 ? null :
            CodeRulesArchitecture.Read(start.Compilation, config, errors, notices, batches, start.CancellationToken);
        var trees = 0;
        start.RegisterSemanticModelAction(context =>
        {
            if (config is null || architecture is null || errors.Count != 0)
            {
                return;
            }

            var path = config.Relative(context.SemanticModel.SyntaxTree.FilePath);
            if (path is null || !path.StartsWith("src/Kimi/", StringComparison.Ordinal))
            {
                return;
            }

            var source = new CodeRulesSource(context.SemanticModel, path, context.CancellationToken);
            new FileWalker(source, architecture, config, context.ReportDiagnostic, context.CancellationToken).Analyze();

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

            foreach (var notice in notices)
            {
                context.ReportDiagnostic(notice);
            }

            var observations = new List<CodeRuleObservation>(batches.Sum(x => x.Count));
            foreach (var batch in batches)
            {
                observations.AddRange(batch);
            }

            batches.Clear();
            baseline.Complete(observations, config.Mode, context.ReportDiagnostic, context.CancellationToken);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (config.Mode != "Enforce")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CodeRule.Complete,
                    Location.None,
                    config.Checksums.Add("treeCount", trees.ToString(CultureInfo.InvariantCulture)).Add("annotationKinds", "exception,transitional")));
            }
        });
    }
}
