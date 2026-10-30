// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The records of the conformance effect check (SPEC 8.11.3): the first violating effect of each conformance a bound rejects,
// the effect kinds shared with the Callable and virtual checks, and their publication.
public sealed partial class Binding
{
    // SPEC 8.11.1: the first violating effect of each conformance a bound rejects, keyed by its Self clause; recorded only
    // when one is rejected and reused across passes.
    private Dictionary<Koto, EffectViolationRecord>? effectViolations;

    /// <summary>SPEC 8.11.1: the kind of the first effect of an implementation that violates a bound.</summary>
    internal enum EffectViolation : byte
    {
        /// <summary>No violation.</summary>
        None,

        /// <summary>An access to a mutable static Field, which confined excludes.</summary>
        MutableStatic,

        /// <summary>A standard operation on state outside the program, such as Console output, which confined excludes.</summary>
        ExternalOperation,

        /// <summary>A call of a foreign function, an environment effect that confined excludes (SPEC 22.6).</summary>
        ForeignCall,

        /// <summary>A read of a raw pointer from an immutable static, which obtains environment authority.</summary>
        StaticPointer,

        /// <summary>A conversion of an integer to a pointer, which obtains environment authority.</summary>
        IntegerPointer,

        /// <summary>A call whose effects cannot be classified, treated as a conflict.</summary>
        UnclassifiedCall,

        /// <summary>A requirement call that no available bound covers, treated as a conflict.</summary>
        UnboundedRequirement,

        /// <summary>A dynamic virtual call whose public slot does not guarantee the required bound.</summary>
        UnboundedVirtual,

        /// <summary>A destruction whose effects cannot be classified, treated as a conflict.</summary>
        UnknownDestruction,

        /// <summary>An abstract callable has no premise declaring the required bound.</summary>
        MissingCallablePremise,

        /// <summary>An erased Function Type keeps no effect bound.</summary>
        ErasedCallable,

        /// <summary>An effect this implementation cannot classify yet: the check fails as an implementation limit (Unsupported_Kd).</summary>
        Limit,
    }

    /// <summary>
    /// SPEC 8.11.1: reports a conformance that a bound rejects at the first violating effect in the implementation's own body.
    /// The Reason names the violation, the bound and its declaring Contract; the Note tells a definite violation from an
    /// unknown effect counted as a conflict; the related locations give the effect, the conformance and the bound.
    /// </summary>
    /// <param name="use">The Self clause of the conformance.</param>
    /// <param name="requirement">The failed requirement of the record.</param>
    /// <param name="code">The code to report.</param>
    /// <returns><see langword="true"/> when a violation was recorded for the conformance and reported.</returns>
    internal bool ReportEffectViolation(Koto use, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (this.effectViolations?.TryGetValue(use, out var violation) != true)
        {
            return false;
        }

        var name = violation.Requirement.Name;
        var item = this.DeclaredEffectBound(violation.Contract, violation.Identity);
        var contract = item is null ? violation.Contract.Symbol.Name : DeclaringContract(item)?.BoundSymbol?.Name ?? violation.Contract.Symbol.Name;
        var spelling = Constants.ConfinedKeyword;

        // SPEC 8.11.1: an environment effect is named by the operation that obtained the authority (SPEC 8.11.2).
        var effect = EffectCause(violation.Kind);
        var definite = violation.Kind is EffectViolation.MutableStatic or EffectViolation.ExternalOperation or EffectViolation.ForeignCall or
            EffectViolation.StaticPointer or EffectViolation.IntegerPointer;

        var note = definite
            ? $"{name} must satisfy {spelling}, declared by {contract}, and this effect violates it"
            : $"{name} must satisfy {spelling}, declared by {contract}; this effect cannot be classified, so verification treats it as a conflict (SPEC 8.6)";

        var count = (violation.Node is { } node && !ReferenceEquals(node, violation.Site) ? 1 : 0) + 1 + (item is null ? 0 : 1);
        var related = new (string Role, Koto At, string? Label)[count];
        var next = 0;
        if (violation.Node is { } effectNode && !ReferenceEquals(effectNode, violation.Site))
        {
            related[next++] = ("effect", effectNode, "the violating effect");
        }

        related[next++] = ("conformance", use, "the conformance checked against the bound");
        if (item is not null)
        {
            related[next] = ("bound", item, "the bound");
        }

        use.Report(requirement, code, note: note, at: violation.Site, evidence: [$"{effect}, which {spelling} excludes"], related: related);
        return true;
    }

    // Share cause text across Contract, Callable and virtual implementation diagnostics.
    private static string EffectCause(EffectViolation kind, bool callable = false) => kind switch
    {
        EffectViolation.MutableStatic => "a mutable static access",
        EffectViolation.ExternalOperation => "an external operation",
        EffectViolation.ForeignCall => "a foreign function call",
        EffectViolation.StaticPointer => "a raw pointer read from an immutable static",
        EffectViolation.IntegerPointer => "a pointer made from an integer",
        EffectViolation.UnboundedRequirement => callable ? "a requirement call without the required guarantee" : "a requirement call with unknown effects",
        EffectViolation.UnboundedVirtual => "a virtual call without the required public effect guarantee",
        EffectViolation.UnknownDestruction => callable ? "destruction with unknown effects" : "a destruction with unknown effects",
        EffectViolation.MissingCallablePremise => "the enclosing premise does not declare the bound",
        EffectViolation.ErasedCallable => "a common Function Type whose erasure keeps no bounds",
        _ => "a call with unknown effects",
    };

    // The violating effect and the public bound it must satisfy; text is formed only during publication.
    private readonly record struct EffectViolationRecord(
        EffectViolation Kind, Koto? Site, Koto? Node, FunctionKoto Requirement, BoundContract Contract, BoundRequirement Identity);
}
