// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace XunitTest;

/// <summary>The one test reader of the records the old path retains after an analysis (R4–R6), which the semantic snapshot prints and
/// the MIR difference ledger compares: Binding's issues, then control flow's, then ownership's, each with its requirement, code and node,
/// and the location text both write. R6 deletes it with the old path.</summary>
internal static class LegacyRecords
{
    /// <summary>The prefix of a library document's location.</summary>
    internal const string Library = "Kimi/";

    /// <summary>Lists the old path's records of the latest analysis: Binding's, control flow's, then ownership's, each in its own order.</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <returns>The records with their requirement (a Binding or ownership failure, or control flow), code and node.</returns>
    internal static IEnumerable<(DiagnosticRequirement Requirement, DiagnosticCode Code, Koto Node)> Of(Compilation compilation)
    {
        foreach (var issue in compilation.Binding.Issues)
        {
            yield return (DiagnosticRequirement.Binding(issue.Failure), issue.Code, issue.Node);
        }

        foreach (var issue in compilation.Ownership.ControlFlow?.Issues ?? [])
        {
            yield return (DiagnosticRequirement.ControlFlow, issue.Code, issue.Node);
        }

        foreach (var issue in compilation.Ownership.Issues)
        {
            yield return (DiagnosticRequirement.Ownership(issue.Failure), issue.Code, issue.Source);
        }
    }

    /// <summary>Gets a node's location as its document and start offset; a library document is <c>Kimi/</c> and its file name.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The location, or <c>?</c> without a node.</returns>
    internal static string Location(Koto? node)
        => node is null ? "?" : Document(node) + ":" + node.Span.Start;

    private static string Document(Koto? node)
    {
        var path = node?.CodeContext?.SourceDocument?.Path;
        return path is null ? "?" : path.StartsWith("compiler://", StringComparison.Ordinal) ? Library + path[(path.LastIndexOf('/') + 1)..] : path;
    }
}
