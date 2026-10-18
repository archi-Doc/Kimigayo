// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the publication of a Final pass's non-failing records (DECISIONS PD31): diagnostics that never fail a node, so Binding's
// completeness and every later phase stay what they are without them.
// Input: the pass's hidden named aliases, unreachable patterns and positions that always fail, each decided by its owner, and
// the HIR lints' records (Binding.Lints).
// Output: records in the Binding partition under the requirement Binding with BindingFailure.None; a lint's condition is its row.
// ReportDiagnostics calls it at a Final pass only. Must not fail a node or decide a record.

using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private void ReportNonFailingRecords()
    {
        var warning = DiagnosticRequirement.Binding(BindingFailure.None);
        foreach (var alias in this.aliasWarnings)
        {
            alias.Report(warning, DiagnosticCode.HiddenNamedAlias_Kd);
        }

        foreach (var pattern in this.patternWarnings)
        {
            pattern.Pattern.Report(warning, DiagnosticCode.UnreachablePattern_Kd, pattern.CoveringArm + 1);
        }

        foreach (var position in this.positionWarnings)
        {
            position.Node.Report(
                warning, DiagnosticCode.PositionAlwaysFails_Kd, position.Kind, evidence: position.FixedLength < 0 ? null : [position.FixedLength]);
        }

        this.ReportLints();
    }
}
