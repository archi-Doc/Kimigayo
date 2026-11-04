// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace XunitTest;

/// <summary>The one test reader of the records the old path retains after an analysis (R4–R6), which the semantic snapshot prints and
/// the old analysis's own tests read: Binding's issues and HIR lint Errors, then control flow's, then ownership's, each with its
/// requirement, code and node. R6-check H10b deletes it with the old path.</summary>
internal static class LegacyRecords
{
    /// <summary>Lists the old path's records of the latest analysis: Binding's (its HIR lint Errors after its issues), control flow's, then
    /// ownership's, each in its own order.</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <returns>The records with their requirement (a Binding or ownership failure, or control flow), code and node.</returns>
    internal static IEnumerable<(DiagnosticRequirement Requirement, DiagnosticCode Code, Koto Node)> Of(Compilation compilation)
    {
        foreach (var issue in compilation.Binding.Issues)
        {
            yield return (DiagnosticRequirement.Binding(issue.Failure), issue.Code, issue.Node);
        }

        foreach (var (node, code) in compilation.Binding.LintErrorRecords)
        {
            yield return (DiagnosticRequirement.Binding(BindingFailure.None), code, node);
        }

        foreach (var issue in compilation.AnalyzeLegacyOwnership().ControlFlow?.Issues ?? [])
        {
            yield return (DiagnosticRequirement.ControlFlow, issue.Code, issue.Node);
        }

        foreach (var issue in compilation.AnalyzeLegacyOwnership().Issues)
        {
            yield return (DiagnosticRequirement.Ownership(issue.Failure), issue.Code, issue.Source);
        }
    }
}
