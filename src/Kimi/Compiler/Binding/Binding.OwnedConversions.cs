// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the failure of a common Function conversion whose subject is not proven Owned (SPEC 7.7.4, 15.2.3), a Constraint failure: one
// on a failed part (an Error's cause, or the failed operand context of a subject no work judged) is derived from it (SPEC 23.1.2,
// DECISIONS BU-75), else the record explains the member of the subject's OwnedOrigins through which a non-static Origin enters (an
// OwnedConversionFact), chosen where the erasure decides (OwnedMember), at the converted value (DECISIONS BU-107). Before the capability
// engine judges (an inferred Field's header), a subject no work judged is the located limit. A call's applicability reads the same
// fact for its erased arguments (ErasedArgumentsEnvironment, SPEC 10.1 step 5).
// Input: the erasure's Owned proof with its rest (the subject's work, whose parts the capability engine records at every path,
// CapabilityWork.Part; R4-M161), each leaf Origin judged by the relation judge at the use (JudgeOriginRelation with `static` as the
// shorter end, SPEC 15.6.5); the call's retention for the result's independence of the receiver (ReceiverIndependence); the erasure's
// outcome for the converted Closure or Function Item and the required Function Type (ErasureOf), the use.
// Output: the explaining member and the Owned fact (Refuted, Final); the use failed, or false when the erasure failed another part; a
// derived use, settled in Binding.Check's Final prologue (SettleDerivedUses); a candidate's erased arguments' environment verdict, with
// a ComponentSite at each limited argument (LimitErasedArgument, BU-22's L). The record must not prove Owned again or select a
// conversion.
public sealed partial class Binding
{
    // Whether an Origin, or an operand of its meet, is a local region `body` judges (RegionIn): the region of the erasure's Final class
    // (RefutedStatic).
    private static bool RegionLeaf(BoundOrigin origin, ExecutableBody body)
    {
        for (var i = 0; origin.Kind == OriginKind.Intersection && i < origin.Operands.Count; i++)
        {
            if (RegionLeaf(origin.Operands[i], body))
            {
                return true;
            }
        }

        return RegionIn(origin, body);
    }

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

        // ErasureSubject's member i, in the callable's own numbering (CapabilityParts): a Closure's capture i; an Item's bound argument,
        // its Contract reference's Type and Origin arguments being that reference's; a nested definition's inherited argument (member
        // Components.Count, SPEC 6.1.3; InheritedArguments, readable since the erasure judged it) named by itself, a bound generic
        // argument of a Closure too. An inherited Origin has no Type: its member Type is the subject itself, which the report reads as an
        // Origin that is not static (SPEC 15.2.3).
        var (part, partOrigin, index) = CapabilityParts.At(implementation, this.InheritedArguments(implementation) ?? BoundType.Unit, i);
        var inherited = index == implementation.Components.Count;
        var closure = !inherited && implementation is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto declaration } ? declaration : null;
        var captures = inherited ? null : BoundClosure.Of(implementation)?.Captures;
        var member = inherited ? part ?? implementation : implementation.Components[index];
        var name = captures is not null ? captures[i].Environment.Name
            : inherited ? (part is null ? $"inherited Origin `{partOrigin!.Name}`" : $"inherited Type argument `{DiagnosticTypeName(part)}`") : $"{Ordinal(index + 1)} Type argument";

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

    // A part walk's visited works (OwnedMember), kept across walks so a warm rebind allocates nothing.
    private readonly HashSet<CapabilityWork> ownedWalk = new(ReferenceEqualityComparer.Instance);

    // SPEC 7.7.4's second bullet, 10.1 step 5, 15.6.1: the erased arguments' environment conditions at applicability, each argument's
    // verdict computed once (ErasedArgumentEnvironment): a refuted argument makes the candidate inapplicable whatever another argument's
    // limit says; else a limited argument is the located limit at that argument (LimitErasedArgument); else every verdict is judged
    // after selection, an Unknown an obligation.
    private ConstraintProof ErasedArgumentsEnvironment(ReadOnlySpan<BoundArgumentOperation> operations)
    {
        var limits = this.flagScratch.Rent(operations.Length); // Whether each argument's verdict is the limit.
        try
        {
            var limited = false;
            for (var i = 0; i < operations.Length; i++)
            {
                var verdict = this.ErasedArgumentEnvironment(operations[i]);
                if (verdict == ConstraintProof.Refuted)
                {
                    return ConstraintProof.Refuted;
                }

                limits[i] = verdict == ConstraintProof.Error;
                limited |= limits[i];
            }

            for (var i = 0; limited && i < operations.Length; i++)
            {
                if (limits[i])
                {
                    this.LimitErasedArgument(operations[i].Source!);
                }
            }

            return limited ? ConstraintProof.Error : ConstraintProof.Proven;
        }
        finally
        {
            this.flagScratch.Return(limits, clearArray: true);
        }
    }

    // An argument's environment condition, the erasure row's verdict at applicability and the one fact the record reads too
    // (ErasesToFunction): its complete Owned fact (ProveErasedOwned, OwnedMember) and its result's independence of the receiver
    // (ReceiverIndependence). Refuted: the engine's own Refuted (a decided leaf: raw storage's TryLeafCapability row, a summary excluding
    // Owned), a refuted leaf or a dependence; Error: a part decided only once the body is bound (Final) or a retention past its limits;
    // else Proven, its verdict judged after selection. An argument that is no Closure's or Function Item's erasure is Proven.
    private ConstraintProof ErasedArgumentEnvironment(in BoundArgumentOperation operation)
    {
        if (operation is not { Adaptation: ArgumentAdaptation.Erasure, Source: { } source, SourceType: { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } actual })
        {
            return ConstraintProof.Proven;
        }

        var held = this.failedCause;
        var proof = this.ProveErasedOwned(actual, source, out var owned);
        this.failedCause = held;
        var independence = this.ReceiverIndependence(actual);
        return proof == ConstraintProof.Refuted || (proof == ConstraintProof.Unknown && owned.Refuted) || independence == ConstraintProof.Refuted ? ConstraintProof.Refuted
            : (proof == ConstraintProof.Unknown && owned.Final) || independence == ConstraintProof.Error ? ConstraintProof.Error
            : ConstraintProof.Proven;
    }

    // The located limit at a limited erased argument (BU-22's L): a site of its own at `source`, each such argument its own (SPEC
    // 23.1.2); the call rests on the first.
    private void LimitErasedArgument(Koto source)
    {
        var site = new ComponentSite(this.neededSites) { Use = source };
        if (this.failedCause is null)
        {
            this.failedCause = site;
        }
        else
        {
            site.Rest();
        }
    }

    // SPEC 7.7.4, 15.2.3: the erasure's one Owned proof (ErasesToFunction), of its subject (ErasureSubject), the channel cleared before it;
    // an Error's cause is held for the conversion's record (FailOwnedConversion), never rested here, and a failure names its member
    // (OwnedMember).
    private ConstraintProof ProveErasedOwned(BoundType actual, Koto node, out OwnedFact owned)
    {
        this.failedCause = null;
        var proof = this.ProveOwned(this.ErasureSubject(actual), node, out var rest);
        owned = proof == ConstraintProof.Proven ? default : this.OwnedMember(proof == ConstraintProof.Error ? this.HoldCause() : null, rest, node);
        return proof;
    }

    // SPEC 7.7.4, 15.2.3 (DECISIONS BU-73): an erasure's subject, its callable's complete environment, its parts (CapabilityParts) as one
    // environment Tuple (EnvironmentTuple): a Closure's captures, whose members are their indices, or a Function Item's bound arguments, a
    // requirement Item's Contract-reference Origin arguments included, then a nested definition's inherited arguments (InheritedArguments,
    // SPEC 6.1.3). Its call signature, and an enclosing function's Origin that a nested function's signature names (InheritedEnvironment),
    // are the required Function Type's to state, never a part of the proof. A callable without parts has the empty environment, Unit,
    // which no work needs to judge, also before the engine judges (an inferred Field's header, SPEC 11.6). A nested definition whose
    // instance is withheld is its own subject: its Owned proof answers the located limit (StructuralCapability).
    private BoundType ErasureSubject(BoundType callable)
    {
        if (callable.Kind is not (BoundTypeKind.Closure or BoundTypeKind.FunctionItem) || this.InheritedArguments(callable) is not { } inherited)
        {
            return callable;
        }

        var t = 0;
        var o = 0;
        foreach (var (part, _, _) in new CapabilityParts(callable, inherited))
        {
            (t, o) = part is null ? (t, o + 1) : (t + 1, o);
        }

        var types = this.RentTypes(t);
        var origins = this.originScratch.Rent(o);
        try
        {
            (t, o) = (0, 0);
            foreach (var (part, origin, _) in new CapabilityParts(callable, inherited))
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

    // SPEC 15.2.3, 15.6.5 (BU's n6; G379 R4, R5): the Owned engine's complete fact over the subject's parts that are not Proven, at any
    // depth (a part with a Type through its own work's parts, OwnedWork; every engine path records its parts, CapabilityWork.Part), each
    // leaf Origin classified at `use` (RefutedStatic): Refuted when one is; else Final when one is decided only once the body is bound,
    // or when no part is known: no work judged the subject, or its work was forgotten (its evaluation read an open obligation,
    // ForgetPendingCapabilities; rulings-g 3). The member it names is the first member with a refuted leaf, with that leaf, else the
    // first member with its first Origin. -1 when no part failed; -2 when no work judged the subject.
    private OwnedFact OwnedMember(ICheckCause? cause, in OwnedRest rest, Koto use)
    {
        var first = new OwnedFact(cause, rest.Work is null ? -2 : -1, null, false, false);
        var refuted = first;
        var final = rest.Work is null or { Pending: true };
        var parts = rest.Work?.Parts;
        this.ownedWalk.Clear();
        for (var p = 0; parts is not null && p < parts.Count; p++)
        {
            var (member, origin, type) = parts[p];
            var leaf = this.RefutedLeaf(type, origin, rest.Scope, use, ref final);
            if (first.Member < 0 || member < first.Member)
            {
                first = new(cause, member, origin, false, false);
            }

            if (leaf is not null && (refuted.Member < 0 || member < refuted.Member))
            {
                refuted = new(cause, member, leaf, true, false);
            }
        }

        return (refuted.Member >= 0 ? refuted : first) with { Final = final };
    }

    // The first leaf Origin refuted under a part that is not Proven: an Origin part is its leaf; a part with a Type, each part of its
    // work, at any depth; a Type without a work (a leaf the engine decided, or a limit) and a limit part keep the engine's verdict.
    // `final` collects a leaf decided only once the body is bound.
    private BoundOrigin? RefutedLeaf(BoundType? type, BoundOrigin? origin, BindingScope scope, Koto use, ref bool final)
    {
        if (type is null)
        {
            return origin is not null && this.RefutedStatic(origin, use, ref final) ? origin : null;
        }

        if (this.OwnedWork(type, scope) is not { Result: not ConstraintProof.Proven } work || !this.ownedWalk.Add(work))
        {
            return null; // A recursive Type's cycle adds no leaf (SPEC 15.3.5).
        }

        for (var i = 0; work.Parts is { } parts && i < parts.Count; i++)
        {
            if (this.RefutedLeaf(parts[i].Type, parts[i].Origin, scope, use, ref final) is { } leaf)
            {
                return leaf;
            }
        }

        return null;
    }

    // SPEC 15.6.5: whether a leaf Origin cannot outlive `static`, read from the relation's judge in the forming body at `use`
    // (JudgeOriginRelation with `static` as the shorter end: the Origin at the use, a local region through its recorded sources,
    // JudgeLocalRegion, an atom by JudgeOriginAtoms); a Refuted verdict is final (a recorded bound is never removed). A leaf with a local
    // region the body judges that is not refuted is decided only once the body is bound (`final`: its later bounds may refute it); any
    // other leaf keeps the engine's verdict.
    private bool RefutedStatic(BoundOrigin origin, Koto use, ref bool final)
    {
        if (this.JudgeOriginRelation(origin, BoundOrigin.Static, use, this.formingBody) == OriginJudgment.Refuted)
        {
            return true;
        }

        final |= this.formingBody.IsBody && RegionLeaf(this.OriginAtUse(origin, use), this.formingBody);
        return false;
    }
}
