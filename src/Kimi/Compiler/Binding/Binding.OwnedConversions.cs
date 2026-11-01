// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the failure of a common Function conversion whose subject is not proven Owned (SPEC 7.7.4, 15.2.3), a Constraint failure: one
// on a failed part (an Error's cause, or the failed operand context of a subject no work judged) is derived from it (SPEC 23.1.2,
// DECISIONS BU-75), else the record explains the member of the subject's OwnedOrigins through which a non-static Origin enters (an
// OwnedConversionFact), chosen where the erasure decides (OwnedMember).
// Input: the erasure's Owned proof with its rest (the capability engine's parts that are not Proven, R4-M161); the erasure's outcome for
// the converted Closure or Function Item and the required Function Type (ErasureOf), the use.
// Output: the explaining member; the use failed, or false when the erasure failed another part. Must not prove Owned again or select a
// conversion.
public sealed partial class Binding
{
    // SPEC 15.2.3, 7.7.4: the erasure decided which part failed and which member explains it (ErasesToFunction); a recorded mismatch of a
    // Closure or Item against a Function Type always follows its erasure, so a missing outcome is a broken contract. An Owned failure that
    // no work judged returned on its subject's failed operand context (RequestCapability). The member is a capture of a closure or a bound
    // Type argument of an Item.
    private bool FailOwnedConversion(BoundType implementation, BoundType required, Koto at, Koto use)
    {
        if (this.ErasureOf(implementation, required) is not { } erasure)
        {
            this.Fail(use, BindingFailure.Internal);
            return true;
        }

        if (erasure.Part != ErasurePart.Owned)
        {
            return false;
        }

        if (erasure.Owned.Cause is { } held)
        {
            held.Rest(); // SPEC 23.1.2: the failure is derived from the cause the erasure held.
            if (held.Use is { } cause)
            {
                this.FailDerived(use, cause);
                return true;
            }
        }

        if (erasure.Owned.Member == -2)
        {
            this.FailDerived(use, this.InvalidOperandContext(implementation));
            return true;
        }

        if (erasure.Owned is not { Member: >= 0 and var i, Origin: var origin } owned)
        {
            return false; // No part failed: the failure rests on no member (the Type mismatch keeps its Note).
        }

        var closure = implementation is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto declaration } ? declaration : null;
        var captures = BoundClosure.Of(implementation)?.Captures;
        var member = implementation.Components[i]; // A Closure's environment Type is built from its captures (Storage).
        var name = captures is null ? $"{Ordinal(i + 1)} Type argument" : captures[i].Environment.Name;

        // A captured binding initialized by a Borrow shows that Borrow (SPEC 15.6.5).
        var borrow = captures is not null && origin is not null && captures[i].Source.Declaration is VariableKoto { InitializerKoto: { } initializer } &&
            KotoHelper.UnwrapParentheses(initializer) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } source && ReferenceEquals(source.BoundType?.Origin, origin) ? source : null;
        var entry = closure is null ? null : CaptureEntryOf(closure, name);
        var fact = new OwnedConversionFact(at, implementation, name, member!, origin, entry, closure, borrow, owned.Refuted);
        this.FailExplained(ref this.ownedConversions, use, fact.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, fact);
        return true;

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // SPEC 23.1.2 (DECISIONS BU-75): a conversion whose own check failed because a part it judged failed is a derived failure, with no
    // record: the part is its prerequisite, and IsDerived's row (InvalidConstraint whose part is Invalid) classifies it. Invariant: the
    // part the use rests on is Invalid: a held cause's use failed, an operand context is Invalid by construction (InvalidOperandContext:
    // InvalidDeclarationContextCause returns only under an Invalid declaration), and no site rests the use on another part first. Any
    // other case, including a subject no work judged without a failed context, is a broken contract (InternalInvariant_Kd).
    private void FailDerived(Koto use, Koto? part)
    {
        if (part is not null)
        {
            this.AddPrerequisite(use, part);
        }

        var derived = part is not null && this.partPrerequisites.TryGetValue(use, out var rest) && rest.BindingState == BindingState.Invalid;
        this.Fail(use, derived ? BindingFailure.InvalidConstraint : BindingFailure.Internal);
    }

    // SPEC 7.7.4, 15.2.3: the erasure's one Owned proof (ErasesToFunction), the channel cleared before it; an Error's cause is held for
    // the conversion's record (FailOwnedConversion), never rested here, and a failure names its member (OwnedMember).
    private ConstraintProof ProveErasedOwned(BoundType actual, Koto node, out OwnedFact owned)
    {
        this.failedCause = null;
        var proof = this.ProveOwned(actual, node, out var rest);
        owned = proof == ConstraintProof.Proven ? default : this.OwnedMember(proof == ConstraintProof.Error ? this.HoldCause() : null, rest);
        return proof;
    }

    // SPEC 15.2.3, 15.6.1 (BU's n6): the member an Owned failure names, from the parts the engine found not Proven: each member in member
    // order with the Origin of its first part; the first whose Origin is Refuted in the forming body (a body-local borrow) wins, else the
    // first. -1 when no part failed; -2 when no work judged the subject.
    private OwnedFact OwnedMember(ICheckCause? cause, in OwnedRest rest)
    {
        var first = new OwnedFact(cause, rest.Judged ? -1 : -2, null, false);
        var refuted = first;
        var parts = rest.Parts;
        for (var p = 0; parts is not null && p < parts.Count; p++)
        {
            var (member, origin) = parts[p];
            var earlier = false;
            for (var q = 0; q < p && !earlier; q++)
            {
                earlier = parts[q].Member == member;
            }

            if (earlier)
            {
                continue; // A member is explained by its first part.
            }

            var fact = new OwnedFact(cause, member, origin, origin is not null && BodyLocalIn(origin, this.formingBody));
            first = first.Member < 0 || member < first.Member ? fact : first;
            refuted = fact.Refuted && (refuted.Member < 0 || member < refuted.Member) ? fact : refuted;
        }

        return refuted.Member >= 0 ? refuted : first;
    }
}
