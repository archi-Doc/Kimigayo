// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the failure of a common Function conversion whose subject is not proven Owned (SPEC 7.7.4, 15.2.3), a Constraint failure: an
// Error rests on its cause (SPEC 23.1.2), else the record explains the first member of the subject's OwnedOrigins through which a
// non-static Origin enters (an OwnedConversionFact).
// Input: the erasure's outcome for the converted Closure or Function Item and the required Function Type (ErasureOf), the use.
// Output: the use failed, or false when the erasure failed another part. Must not prove Owned or select a conversion, except the
// member loop below until r2-1b.
public sealed partial class Binding
{
    // SPEC 15.2.3, 7.7.4: the erasure decided which part failed (ErasesToFunction); a recorded mismatch of a Closure or Item against a
    // Function Type always follows its erasure, so a missing outcome is a broken contract. The explaining member is a capture of a
    // closure or a bound Type argument of an Item; the first Refuted member (a body-local Origin) wins, else the first Unknown one.
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

        if (erasure.Cause is { } held)
        {
            held.Rest(); // SPEC 23.1.2: the record rests on the cause the erasure held.
            if (held.Use is { } cause)
            {
                this.CompleteDependent(use, cause);
                return true;
            }
        }

        // TEMPORARY (G216 (a) U2.1 r2-1a, removed by r2-1b): the member is found by proving each member again, the one re-proof left,
        // until the erasure publishes the explaining member and its Origin from the Owned proof that decided (PLAN G216).
        var closure = implementation is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto declaration } ? declaration : null;
        var captures = BoundClosure.Of(implementation)?.Captures;
        OwnedConversionFact? unknown = null;
        var count = captures?.Count ?? implementation.Components.Count;
        for (var i = 0; i < count; i++)
        {
            var member = captures is null ? implementation.Components[i]
                : implementation.Components.Count == captures.Count ? implementation.Components[i] : captures[i].Environment.Type;
            if (member is null || this.ProveOwned(member, use) == ConstraintProof.Proven)
            {
                continue;
            }

            var origin = this.FirstUnownedOrigin(member, use, 0);
            var name = captures is null ? $"{Ordinal(i + 1)} Type argument" : captures[i].Environment.Name;

            // A captured binding initialized by a Borrow shows that Borrow (SPEC 15.6.5).
            var borrow = captures is not null && origin is not null && captures[i].Source.Declaration is VariableKoto { InitializerKoto: { } initializer } &&
                KotoHelper.UnwrapParentheses(initializer) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } source && ReferenceEquals(source.BoundType?.Origin, origin) ? source : null;
            var local = origin is not null && BodyLocalIn(origin, this.formingBody);
            var fact = new OwnedConversionFact(at, implementation, name, member, origin, closure is null ? null : CaptureEntryOf(closure, name), closure, borrow, local);
            if (fact.Refuted)
            {
                unknown = fact;
                break;
            }

            unknown ??= fact;
        }

        if (unknown is not { } failure)
        {
            return false;
        }

        this.FailExplained(ref this.ownedConversions, use, failure.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, failure);
        return true;

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // SPEC 7.7.4, 15.2.3: the erasure's one Owned proof (ErasesToFunction), the channel cleared before it; an Error's cause is held for
    // the conversion's record (FailOwnedConversion), never rested here.
    private ConstraintProof ProveErasedOwned(BoundType actual, Koto node, out ICheckCause? cause)
    {
        this.failedCause = null;
        var owned = this.ProveOwned(actual, node);
        cause = owned == ConstraintProof.Error ? this.HoldCause() : null;
        return owned;
    }

    // The first Origin in a Type's OwnedOrigins, in the order of SPEC 15.2.3, that is not proven static; a common Function Type
    // contributes no per-call Origin (SPEC 15.2.3).
    private BoundOrigin? FirstUnownedOrigin(BoundType type, Koto use, int depth)
    {
        if (depth > 32)
        {
            return null;
        }

        if (OwnedOrigin(type) is { } origin && !this.ProvesOriginOutlives(origin, BoundOrigin.Static, use))
        {
            return origin;
        }

        for (var i = 0; i < type.Components.Count && type.Kind != BoundTypeKind.Function; i++)
        {
            if (this.FirstUnownedOrigin(type.Components[i], use, depth + 1) is { } inner)
            {
                return inner;
            }
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (type.OriginArguments[i] is { } argument && !this.ProvesOriginOutlives(argument, BoundOrigin.Static, use))
            {
                return argument;
            }
        }

        return null;
    }
}
