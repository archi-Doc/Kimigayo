// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Converts a failed match's retained coverage to its diagnostic. Binding.Patterns alone judges coverage; this adapter reads
// the reason and Case index without visiting Patterns or proving coverage again.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private void ReportMatchCoverage(MatchKoto node, DiagnosticRequirement requirement)
    {
        // Only a match plan fails NonExhaustiveMatch, so its coverage is always known.
        var coverage = this.matches[node].Coverage;
        var note = coverage.Requirement switch
        {
            MatchCoverageReason.MissingCase => $"The Case at declaration index {coverage.CaseIndex} has no arm.",
            MatchCoverageReason.WholePayloadRequired => $"The Case at declaration index {coverage.CaseIndex} requires an unguarded whole-payload arm.",
            MatchCoverageReason.BooleanValuesRequired => "Unguarded true and false Patterns or a catch-all are required.",
            _ => "An unguarded whole-position catch-all is required.",
        };
        object[] evidence = coverage.Requirement is MatchCoverageReason.MissingCase or MatchCoverageReason.WholePayloadRequired
            ? [coverage.Requirement, coverage.CaseIndex] : [coverage.Requirement];
        node.Report(requirement, DiagnosticCode.NonExhaustiveMatch_Kd, note: note, evidence: evidence);
    }
}
