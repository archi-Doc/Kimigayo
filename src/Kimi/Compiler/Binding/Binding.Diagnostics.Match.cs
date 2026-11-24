// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Converts a failed match's coverage record to its diagnostic. Binding.Patterns alone judges coverage and records what the match lacks
// as a Reason, the Case's declaration index plus one packed above its byte (R7-E3 E3c-2); this adapter reads that Reason without visiting
// Patterns or proving coverage again. Its Notes are pinned (PatternBindingTest.CoverageUsesOnlyTheSpecifiedProofs' console row).

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static void ReportMatchCoverage(Koto node, DiagnosticCollection? store, in BindingProblem problem, long reason)
    {
        var requirement = (MatchCoverageReason)(byte)reason;
        var caseIndex = (int)(reason >> 8) - 1;
        var note = requirement switch
        {
            MatchCoverageReason.MissingCase => $"The Case at declaration index {caseIndex} has no arm.",
            MatchCoverageReason.WholePayloadRequired => $"The Case at declaration index {caseIndex} requires an unguarded whole-payload arm.",
            MatchCoverageReason.BooleanValuesRequired => "Unguarded true and false Patterns or a catch-all are required.",
            _ => "An unguarded whole-position catch-all is required.",
        };
        object[] evidence = requirement is MatchCoverageReason.MissingCase or MatchCoverageReason.WholePayloadRequired ? [requirement, caseIndex] : [requirement];
        node.ReportIn(store, problem.Requirement, problem.Code, note: note, evidence: evidence, condition: problem.Condition);
    }
}
