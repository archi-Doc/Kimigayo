// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<FunctionKoto, VirtualEffectViolation>? virtualEffectViolations;

    private readonly record struct VirtualEffectViolation(FunctionKoto Original, EffectBoundKoto Bound, EffectViolation Kind, Koto? Site, Koto? Node);

    internal void ValidateVirtualDestructionEffects(List<Koto> rejected)
    {
        this.virtualEffectViolations?.Clear();
        this.destructionSummary?.BeginPass();
        this.ValidateVirtualEffectBounds(rejected);
    }

    internal void ReportVirtualEffectViolation(FunctionKoto function, DiagnosticRequirement requirement)
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
            related: related);
    }

    // The ownership pass adds exactly the destructions in the completed common body plans.
    private void ValidateVirtualEffectBounds(List<Koto>? rejected = null)
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (function.BindingFailure != BindingFailure.None || function.BoundSymbol is not { } symbol ||
                (rejected is not null && this.compilation.Ownership.TemplateBody(function, false) is not { IsVerified: true }))
            {
                continue;
            }

            var original = function.IsVirtual ? function : this.virtualOverrides.TryGetValue(function, out var entry) ? entry.Slot.Original : null;
            if (original is null || !this.virtualEffectBounds.TryGetValue(original, out var bounds) || (bounds.Confined is null && bounds.Preserves is null))
            {
                continue;
            }

            var summary = rejected is null ? this.effectSummary ??= new(this) : this.destructionSummary ??= new(this);
            var scope = this.scopes[function];
            if (summary.Check(bounds.Confined is not null, bounds.Preserves is not null, symbol, scope.ImplementationPremises ?? scope, rejected is not null, implementationBody: true))
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
            if (rejected is null)
            {
                this.Fail(function, BindingFailure.VirtualEffectBound);
            }
            else
            {
                rejected.Add(function);
            }
        }
    }
}
