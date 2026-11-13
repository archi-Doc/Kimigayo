// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the failure of a common Function conversion whose subject is not proven Owned (SPEC 7.7.4, 15.2.3), a Constraint failure: one
// on a failed part (an Error's cause, or the failed operand context of a subject no work judged) is derived from it (SPEC 23.1.2,
// DECISIONS BU-75), else the record explains the member of the subject's OwnedOrigins through which a non-static Origin enters (an
// OwnedConversionFact), chosen where the erasure decides (OwnedMember), at the converted value (DECISIONS BU-107). Before the capability
// engine judges (an inferred Field's header), a subject no work judged is the located limit.
// Input: the erasure's Owned proof with its rest (the capability engine's parts that are not Proven, R4-M161); the erasure's outcome for
// the converted Closure or Function Item and the required Function Type (ErasureOf), the use.
// Output: the explaining member; the use failed, or false when the erasure failed another part; a derived use, settled in Binding.Check's
// Final prologue (SettleDerivedUses). Must not prove Owned again or select a conversion.
public sealed partial class Binding
{
    // The uses FailDerived failed in this pass, each with its held cause's candidates at failure, its first and the rest (none for an
    // operand context, recorded at once), and a re-entry's site, settled in Binding.Check's Final prologue (DECISIONS BU-76; removed
    // once the erasure's Owned failure is decided after the prologue).
    private List<(Koto Use, Koto? First, ICheckCause? Next, ICheckCause? ReentrySite)>? derivedUses;

    // SPEC 15.2.3, 7.7.4: the erasure decided which part failed and which member explains it (ErasesToFunction), which records an Owned
    // failure here at the converted value; a recorded mismatch of a Closure or Item against a Function Type always follows its erasure, so
    // a missing outcome is a broken contract. A use records one failure: its erasure's record, made where it was judged, is that record, so
    // a consumer reporting the same value or a second binding of it (an inferred Field's initializer in PrepareStorage) adds nothing. An
    // Owned failure that no work judged returned on its subject's failed operand context (RequestCapability). The member is a capture of a
    // closure or a bound Type argument of an Item.
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

        if (use.BindingFailure != BindingFailure.None)
        {
            return true;
        }

        if (erasure.Owned.Cause is { } held)
        {
            held.Rest(); // SPEC 23.1.2: the failure is derived from the cause the erasure held.
            this.FailDerived(use, held);
            return true;
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
        // ErasureSubject's member i, in the callable's own numbering (CapabilityParts): a Closure's capture i; an Item's bound argument,
        // its Contract reference's Type and Origin arguments being that reference's, and an inherited parameter (member Components.Count,
        // SPEC 6.1.3) named by its parameter.
        var (part, partOrigin, index) = CapabilityParts.At(implementation, i);
        var inherited = captures is null && index == implementation.Components.Count;
        var member = inherited ? part ?? implementation : implementation.Components[index];
        var name = captures is not null ? captures[i].Environment.Name
            : inherited ? (part is null ? $"inherited Origin `{partOrigin!.Name}`" : $"inherited Type parameter `{part}`") : $"{Ordinal(index + 1)} Type argument";

        // A captured binding initialized by a Borrow shows that Borrow (SPEC 15.6.5).
        var borrow = captures is not null && origin is not null && captures[i].Source.Declaration is VariableKoto { InitializerKoto: { } initializer } &&
            KotoHelper.UnwrapParentheses(initializer) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } source && ReferenceEquals(source.BoundType?.Origin, origin) ? source : null;
        var entry = closure is null ? null : CaptureEntryOf(closure, name);
        var fact = new OwnedConversionFact(at, implementation, name, member!, origin, entry, closure, borrow, owned.Refuted);
        this.FailExplained(ref this.ownedConversions, use, fact.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, fact);
        return true;

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // SPEC 23.1.2 (DECISIONS BU-75, BU-76): a conversion whose own check failed because a part it judged failed is a derived failure, with
    // no record: the part is its prerequisite, and IsDerived's row (InvalidConstraint whose part is Invalid) classifies it. A held cause's
    // use is listed and rests on its first candidate that failed by Binding.Check's Final prologue (SettleDerivedUses), since a held
    // cause's node fails only there (a Type node at the Definition deadline, a hop's clause at its binding's formation, a limit's Use when
    // the prologue fails it), after the erasure ran in body binding. The candidates are read at failure: a summary re-solved in a later
    // epoch may hold another cause by then.
    private void FailDerived(Koto use, ICheckCause held)
    {
        (this.derivedUses ??= []).Add((use, held.Use, held.Next, held.ReentrySite));
        this.Fail(use, BindingFailure.InvalidConstraint);
    }

    // An Owned failure no work judged rests on its subject's failed operand context, Invalid by construction (InvalidOperandContext),
    // listed with no held cause. Without one, before the capability engine judges (an inferred Field's header, which binds its initializer
    // before every capability), the subject was not judged: the use is the located Unsupported_Kd until Owned is judged during header
    // binding (DECISIONS BU-107; R7-D2 d with C2/C6). After, an unjudged subject is one in an unresolved context: a broken contract
    // (InternalInvariant_Kd) at once.
    private void FailDerived(Koto use, Koto? part)
    {
        if (part is not null)
        {
            this.AddPendingPrerequisite(use, part);
            (this.derivedUses ??= []).Add((use, null, null, null));
        }

        var failure = part is not null ? BindingFailure.InvalidConstraint : this.capabilitiesReady ? BindingFailure.Internal : BindingFailure.Unsupported;
        this.Fail(use, failure);
    }

    // DECISIONS BU-76: after every producer's node has failed (Binding.Check's Final prologue, after FailNeededSites), a listed use with no
    // recorded part rests on its held cause's first candidate that failed (a part another site recorded first is kept, BU-61); a use
    // whose part is not Invalid is a broken contract (BU-75), whose InternalInvariant_Kd replaces the use's own InvalidConstraint, which
    // Fail keeps as a node's first failure, except a re-entry none of whose clauses failed: the depth test is no proof of growth, so the
    // use rests on its site, failed here as FailNeededSites fails one (BU-22; removal condition: PLAN G369, an exact re-entry test). A
    // site is a stored Type, which no other use's candidates name unless already Invalid. A resolution's candidates are the hops
    // ContractTypes published, outermost first; a specification with no conformance path adds no hop and forwards its subject's.
    private void SettleDerivedUses()
    {
        for (var i = 0; this.derivedUses is { } uses && i < uses.Count; i++)
        {
            var (use, candidate, next, site) = uses[i];
            while (candidate is not null && !this.partPrerequisites.ContainsKey(use))
            {
                if (candidate.BindingState == BindingState.Invalid)
                {
                    this.partPrerequisites.Add(use, candidate);
                }

                candidate = next?.Use;
                next = next?.Next;
            }

            if (site is not null && !this.partPrerequisites.ContainsKey(use))
            {
                this.Fail(site.Use, BindingFailure.Unsupported);
                this.partPrerequisites.Add(use, site.Use);
            }

            if (!(this.partPrerequisites.TryGetValue(use, out var part) && part.BindingState == BindingState.Invalid))
            {
                this.ClearFailure(use);
                this.Fail(use, BindingFailure.Internal);
            }
        }
    }

    // SPEC 7.7.4, 15.2.3: the erasure's one Owned proof (ErasesToFunction), of its subject (ErasureSubject), the channel cleared before it;
    // an Error's cause is held for the conversion's record (FailOwnedConversion), never rested here, and a failure names its member
    // (OwnedMember).
    private ConstraintProof ProveErasedOwned(BoundType actual, Koto node, out OwnedFact owned)
    {
        this.failedCause = null;
        var proof = this.ProveOwned(this.ErasureSubject(actual), node, out var rest);
        owned = proof == ConstraintProof.Proven ? default : this.OwnedMember(proof == ConstraintProof.Error ? this.HoldCause() : null, rest);
        return proof;
    }

    // SPEC 7.7.4, 15.2.3 (DECISIONS BU-73): an erasure's subject, its callable's complete environment, its parts (CapabilityParts) as one
    // environment Tuple (EnvironmentTuple): a Closure's captures, whose members are their indices, or a Function Item's bound arguments, a
    // requirement Item's Contract-reference Origin arguments included. Its call signature, and what a nested function's Item inherits, are
    // the required Function Type's to state, never a part of the proof. A callable without parts has the empty environment, Unit, which no
    // work needs to judge, also before the engine judges (an inferred Field's header, SPEC 11.6).
    private BoundType ErasureSubject(BoundType callable)
    {
        if (callable.Kind is not (BoundTypeKind.Closure or BoundTypeKind.FunctionItem))
        {
            return callable;
        }

        var t = 0;
        var o = 0;
        foreach (var (part, _, _) in new CapabilityParts(callable))
        {
            (t, o) = part is null ? (t, o + 1) : (t + 1, o);
        }

        var types = this.RentTypes(t);
        var origins = this.originScratch.Rent(o);
        try
        {
            (t, o) = (0, 0);
            foreach (var (part, origin, _) in new CapabilityParts(callable))
            {
                if (part is not null)
                {
                    types[t++] = part;
                }
                else
                {
                    origins[o++] = origin!;
                }
            }

            return this.EnvironmentTuple(types.AsSpan(0, t), origins.AsSpan(0, o));
        }
        finally
        {
            this.typeScratch.Return(types, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
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
