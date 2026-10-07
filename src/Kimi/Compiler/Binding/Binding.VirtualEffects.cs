// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<FunctionKoto, VirtualEffectViolation>? virtualEffectViolations;

    private readonly record struct VirtualEffectViolation(FunctionKoto Original, EffectBoundKoto Bound, EffectViolation Kind, Koto? Site, Koto? Node);

    // Definition-side effects only. Cleanup effects and OCC still need ownership's completed plans.
    private void ValidateVirtualEffectBounds()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (function.BindingFailure != BindingFailure.None || function.BoundSymbol is not { } symbol)
            {
                continue;
            }

            var original = function.IsVirtual ? function : this.virtualOverrides.TryGetValue(function, out var entry) ? entry.Slot.Original : null;
            if (original is null || !this.virtualEffectBounds.TryGetValue(original, out var bounds) || (bounds.Confined is null && bounds.Preserves is null))
            {
                continue;
            }

            var summary = this.effectSummary ??= new(this);
            var scope = this.scopes[function];
            if (summary.Check(bounds.Confined is not null, bounds.Preserves is not null, symbol, scope.ImplementationPremises ?? scope, false, implementationBody: true))
            {
                continue;
            }

            if (summary.ViolationNode is { BindingState: not BindingState.Resolved } cause &&
                (IsRecovery(cause, out _) || cause.BindingFailure != BindingFailure.None || this.HasUnresolvedPrerequisite(cause)))
            {
                this.CompleteDependent(function, cause);
                continue;
            }

            var bound = summary.Violation switch
            {
                EffectViolation.MutableStatic or EffectViolation.ExternalOperation or EffectViolation.ForeignCall or EffectViolation.StaticPointer or EffectViolation.IntegerPointer => bounds.Confined!,
                EffectViolation.ResultLoan => bounds.Preserves!,
                _ => bounds.Preserves ?? bounds.Confined!,
            };
            (this.virtualEffectViolations ??= new(ReferenceEqualityComparer.Instance))[function] = new(original, bound, summary.Violation, summary.ViolationSite, summary.ViolationNode);
            this.Fail(function, BindingFailure.VirtualEffectBound);
        }
    }

    private void ReportVirtualEffectViolation(FunctionKoto function, DiagnosticRequirement requirement)
    {
        var violation = this.virtualEffectViolations![function];
        var spelling = EffectBoundKoto.Spelling(violation.Bound.Bound);
        var related = violation.Node is { } effect && !ReferenceEquals(effect, violation.Site)
            ? new (string Role, Koto At, string? Label)[] { ("declaration", violation.Original, "the original virtual slot"), ("bound", violation.Bound, "the inherited public bound"), ("effect", effect, "the violating effect") }
            : [("declaration", (Koto)violation.Original, (string?)"the original virtual slot"), ("bound", violation.Bound, "the inherited public bound")];
        function.Report(
            requirement,
            DiagnosticCode.UnsatisfiedEffectBound_Kd,
            at: violation.Site,
            evidence: [$"{spelling}: {EffectCause(violation.Kind)}"],
            note: "Every original and override must satisfy the slot's public bounds, including unused declarations; effects that cannot be classified cannot prove the guarantee",
            advice: "Use authority from the inputs and avoid accesses to Loans earlier results may retain. Change a public bound only on the original declaration, after checking its callers",
            related: related);
    }
}
