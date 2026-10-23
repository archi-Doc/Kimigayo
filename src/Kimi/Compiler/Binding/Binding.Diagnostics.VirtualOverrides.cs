// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Converts retained override failures to diagnostic facts and explanations. Inputs are the recorded comparison and locations;
// outputs retain the original requirement, code and primary span. Never select a slot or repeat a signature or premise check.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private void ReportOverrideFailure(FunctionKoto function, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var failure = this.overrideFailures![function];
        var related = failure.First is null ? null : failure.Second is null
            ? new (string, Koto, string?)[] { ("declaration", failure.First, failure.First.IsVirtual ? "original virtual slot" : "inherited function") }
            : [("declaration", failure.First, "original virtual slot"), ("declaration", failure.Second, "conflicting declaration")];
        (object[]? Evidence, string? Note) report = failure.Cause switch
        {
            OverrideFailureCause.NoBase => ([failure.Cause], "this struct has no base from which to inherit a virtual slot"),
            OverrideFailureCause.NoInheritedGroup => ([failure.Cause], "no accessible inherited function group has this Name"),
            OverrideFailureCause.NoMatchingInputs => ([failure.Cause],
                "the first accessible inherited group contains no virtual slot with the same receiver position, mode and input Types"),
            OverrideFailureCause.AmbiguousInputs => ([failure.Cause],
                "multiple distinct virtual declarations have matching inputs after base binding; results, labels and effects do not disambiguate them"),
            OverrideFailureCause.DuplicateSlot => ([failure.Cause], "this derived Type already supplies an implementation of the same bound slot"),
            OverrideFailureCause.ExternalName => ([failure.ExpectedName!, failure.ActualName!, failure.Cause, failure.Parameter],
                "external parameter names must match the original slot; only internal names may change"),
            OverrideFailureCause.ResultType => ([DiagnosticTypeName(failure.ExpectedType!), DiagnosticTypeName(failure.ActualType!), failure.Cause],
                "the result Type must match the original slot; result covariance is not allowed"),
            OverrideFailureCause.OriginBinders => ([failure.Cause],
                "Origin binder names must belong to the original slot; omitted annotations inherit that contract"),
            OverrideFailureCause.Condition when failure.Proof == ConstraintProof.Unknown => ([failure.At!, failure.Cause, failure.Proof],
                $"{failure.At} is not implied by the original slot and derived Type"),
            OverrideFailureCause.Condition when failure.Proof == ConstraintProof.Refuted => ([failure.At!, failure.Cause, failure.Proof],
                $"{failure.At} is refuted by the original slot and derived Type"),
            _ => (null, null),
        };
        var span = failure.Span ?? (failure.At is null ? function.DispatchModifierSpan : null);
        if (report.Evidence is null)
        {
            function.Report(requirement, DiagnosticCode.InternalInvariant_Kd, at: failure.At, span: span);
            return;
        }

        function.Report(requirement, code, at: failure.At, span: span, note: report.Note, evidence: report.Evidence, related: related);
    }
}
