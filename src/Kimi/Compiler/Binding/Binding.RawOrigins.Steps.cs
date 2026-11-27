// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Raw Origins, steps (SPEC 5.7, 5.2.1 condition 3; DECISIONS BU-24, BU-41, BU-46, BU-113; contract in Binding.RawOrigins.cs): the
// authority a raw layer's Origin can keep, which retention and the TyCtxt views read after Binding: by its kind (StepOf), a layer a
// contract's walk meets by its position (RawStepAt, over SignatureStep and BinderStep) and a layer an instance stores (StoredStep and
// its members StoredMembers by a Holdings read's key, StoredStep over StoredLayer, which reads the
// instance's kept part and, by WrittenAt, which TyCtxt publishes, the Type its declaration writes there; the step from the layer its
// declaration states, DeclaredStep, which StoredStep and the hold walk read).
public sealed partial class Binding
{
    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-24; R4's constraint 2): the authority a raw layer's Origin can keep where no declaration
    // states the layer, by its kind: none Unstated, `self` Own, `static` Static, any other Exclusive, never Shared by default, since a
    // borrow formed anywhere may keep an exclusive Loan of what the Origin covers. Unbound, left by a reported error, is Unstated, as is
    // Unstated, a kept part's layer its declaration writes without `during` (DECISIONS BU-46).
    internal static HoldStep StepOf(BoundOrigin? raw) => raw?.Kind switch
    {
        null or OriginKind.Unbound or OriginKind.Unstated => HoldStep.Address,
        OriginKind.Holder => HoldStep.Own,
        OriginKind.Static => HoldStep.Static,
        OriginKind.ConstraintSet => HoldStep.Exclusive, // DECISIONS BU-100: a fixed unknown Origin, as any other.
        _ => HoldStep.Exclusive,
    };

    // SPEC 5.7 Calls ("for self, the holding value's Type"; the owner's Q2 (a)): what a `during self` layer of a stored part admits past
    // it besides its pointee, the one reading of it, which RawHoldings publishes to every reader as row 13's entries: the holding instance's
    // Type arguments by their actual Types (`arguments`), or, where the instance has an Origin argument, the write fact instead (true): any
    // value at each of them.
    internal static bool SelfAdmitted(BoundType? holder, out IReadOnlyList<BoundType> arguments)
    {
        var write = holder is { OriginArguments.Count: > 0 };
        arguments = write || holder is null ? [] : holder.Components;
        return write;
    }

    // DECISIONS BU-58: the Type an instance's declaration writes (WrittenType) at a struct's Field `part`, or at the payload an enum's
    // Case `part` selects by the first position, then at the inline positions below it, while it is a Tuple, a fixed array or a raw layer
    // (Written; positions as for StoredStep): null at or past any other Type (a Type parameter, a pair layer, a nominal, an open
    // projection) and where the declaration writes no such part (`writes` false; an enum's [] is null). Monotone from a part's first
    // positions; it reads the declaration only, never an instance's kept part (StoredLayer reads both), in O(|positions|).
    internal static BoundType? WrittenAt(BoundType instance, int part, ReadOnlySpan<int> positions, out bool writes)
    {
        var adt = instance.Symbol?.Adt;
        var structure = AdtDef.IsStruct(instance);
        var written = WrittenType(structure ? (uint)part < (uint)adt!.Fields.Length ? adt.Fields[part] : null
            : adt is not null && (uint)part < (uint)adt.Cases.Length && adt.Cases[part] is { } selected && positions.Length > 0 &&
                (uint)positions[0] < (uint)selected.Payload.Length ? selected.Payload[positions[0]] : null);
        writes = written is not null;
        for (var i = structure ? 0 : 1; i < positions.Length && written is not null; i++)
        {
            written = InlinePart(written, positions[i]);
        }

        return Written(written);
    }

    // SPEC 5.7, 5.2.1 condition 3 (DECISIONS BU-113, superseding BU-108's gate): the step of a raw layer a contract's walk meets, by its
    // position: a layer a declaration writes (`written`, a Field's or payload's) by DeclaredStep; one of an erased signature (a Function
    // Type's or a Callable bound's, which SPEC 5.2.1 does not cap) by its kind; one at a position the walked signature declares
    // (`signature`, the declared Type there) by the cap over the callee's declared inputs, its Origin read as written, which is where the
    // signature binds it; any other, a Type argument's or a Closure environment's, where its Origin is bound (BinderStep). A declared
    // layer's binder reading is not joined in: where the declaration states its slot by an exclusive Loan, a caller's shared statement
    // does not bound the call's write (M's v2).
    internal HoldStep RawStepAt(BoundType layer, BoundType? written, BoundType? holder, BoundType? signature)
    {
        if (written is { IsPointer: true })
        {
            return this.DeclaredStep(layer, written, holder);
        }

        if (this.retentionInputs == 0)
        {
            return StepOf(OwnedOrigin(layer));
        }

        return !this.BoundElsewhere(written, signature)
            ? this.SignatureStep(SlotArgument(OwnedOrigin(signature!)), CollectionsMarshal.AsSpan(this.retentionDeclared)[..^1])
            : this.BinderStep(OwnedOrigin(layer));
    }

    // SPEC 5.2.1 condition 3 (the owner's 「シグネチャ版も入れる (推奨)」): the step of a raw layer written in a signature whose input
    // Types are `inputs`, read as that signature declares them (DECISIONS BU-113: the walked callee's or a binder's declared inputs, never
    // a call's instantiated ones; none for a Function Type's): Shared when some input states its Origin and every statement of it in the
    // inputs keeps only shared Loans (Statements: the outer Origin of a `ref`, `objref` or Slice layer or of a pair layer admitting only
    // those, an argument of a slot `ref` in every case its Type selects), since a pointer read from the layer then has no write
    // authority; raw layers state no borrow, and borrows formed in a body never count. Any other is its kind (StepOf).
    internal HoldStep SignatureStep(BoundOrigin? raw, ReadOnlySpan<BoundType> inputs)
    {
        var kept = HoldStep.Address;
        for (var i = 0; i < inputs.Length && kept != HoldStep.Exclusive && raw is { Kind: OriginKind.Input or OriginKind.Parameter }; i++)
        {
            kept = this.Statements(inputs[i], raw, kept);
        }

        return kept == HoldStep.Shared ? HoldStep.Shared : StepOf(raw);
    }

    // SPEC 5.2.1 condition 3: the step of the raw layer an instance stores at a part and inline positions (StoredLayer), by DeclaredStep.
    internal HoldStep StoredStep(BoundType instance, int part, ReadOnlySpan<int> positions)
        => this.DeclaredStep(this.StoredLayer(instance, part, positions, out var declared), declared, instance);

    // A5's route to the members of a raw layer an instance stores (PLAN G148 (1), R7 plan Z6): the borrows its Origin under the instance's
    // substitution belongs to, at one arm by `written`: a layer the declaration writes, within the instance (RawMembers: the keepers of
    // the slots the instance binds it to); any other (a Type argument supplies it), by the Origin's binder (AtomMembers: the borrows of the
    // scope that wrote it, a body's or an input's). `self`, `static` and an unstated layer have none. False where the members are
    // unanswered, and at the named limits, by the Origin's limit marks at the layer's arm (LimitsDeclared, LimitsBinder). False appends
    // no member (Z6's R5.4).
    internal bool StoredMembers(BoundType layer, BoundType? written, BoundType holder, List<RawMember> members)
    {
        if (layer.RawOrigin is not { } origin || StepOf(origin) != HoldStep.Exclusive)
        {
            return true;
        }

        var first = members.Count;
        var declared = written is { IsPointer: true };
        var complete = declared ? this.RawMembers(origin, new ReadOnlySpan<BoundType>(in holder), members) : this.AtomMembers(origin, members);
        var range = CollectionsMarshal.AsSpan(members)[first..];
        complete = complete && !(declared ? LimitsDeclared(this.Marks(origin), range) : LimitsBinder(this.Marks(origin), range));
        if (!complete)
        {
            members.RemoveRange(first, members.Count - first);
        }

        return complete;
    }

    // A raw Origin as its signature writes it (DECISIONS BU-113): `x.s` denotes the slot argument of x's declared Type (the declaration's
    // own slot for `self.s`, the projection itself for an input whose Type omits it); any other Origin is itself.
    private static BoundOrigin? SlotArgument(BoundOrigin? origin)
        => origin is { Kind: OriginKind.Input, TargetSlot: >= 0, Occurrence.BoundType: { } instance } &&
            (uint)origin.TargetSlot < (uint)instance.OriginArguments.Count ? instance.OriginArguments[origin.TargetSlot] : origin;

    // The declared slot a raw Origin names: an Input atom's target slot (a projection `x.s` whose slot argument is omitted) with the
    // Type of the input it projects (`instance`), a declaration's own slot (no instance: its declaration's cases); null for any other.
    private static OriginParameter? NamedSlot(BoundOrigin? atom, out BoundType? instance)
    {
        instance = atom?.Kind == OriginKind.Input ? atom.Occurrence?.BoundType : null;
        return atom switch
        {
            { Kind: OriginKind.Input, Occurrence.BoundType.Symbol.Schema: { } schema } when (uint)atom.TargetSlot < (uint)schema.Origins.Count
                => schema.Origins[atom.TargetSlot],
            { Kind: OriginKind.Parameter, Slot: >= 0, Binder: DeclarationContainerKoto { BoundSymbol.Schema: { } schema } }
                when atom.Slot < schema.Origins.Count => schema.Origins[atom.Slot],
            _ => null,
        };
    }

    // The component a Tuple's or fixed array's inline position selects; null for any other Type or none.
    private static BoundType? InlinePart(BoundType? type, int position)
        => type is { Kind: BoundTypeKind.Tuple } && (uint)position < (uint)type.Components.Count ? type.Components[position]
            : type is { Kind: BoundTypeKind.FixedArray } ? type.Components[0] : null;

    // BU-35's `written` (DECISIONS BU-58): a Type a declaration writes while it is a Tuple, a fixed array or a raw layer, the one form
    // WrittenAt and the hold walk (Held) read; null for any other.
    private static BoundType? Written(BoundType? written)
        => written is { IsPointer: true } || written?.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray ? written : null;

    // Whether an Origin is `origin` or a meet with it as an operand.
    private static bool Names(BoundOrigin? stated, BoundOrigin origin)
        => ReferenceEquals(stated, origin) || (stated?.Kind == OriginKind.Intersection && stated.Operands.Contains(origin));

    // The named limits' rule at a declared layer (Z6's C1a, R7.1.2), the members read there given: a fit from another Origin, or a
    // value that may write fitted through a region graph row (the fit's source may carry more authority than the slot's keepers admit, SPEC
    // 5.7 Fits and claims under O-498 Q1 (i); Revision 8), or a claim with an exclusive member (R2.3). An address past a shared path is
    // no limit here: it writes less than the keepers admit (C2).
    private static bool LimitsDeclared(LimitMarks marks, ReadOnlySpan<RawMember> members)
        => (marks & (LimitMarks.FittedIntoHolder | LimitMarks.FittedExclusive)) != 0 || ((marks & LimitMarks.Claimed) != 0 && AnyExclusive(members));

    // The named limits' rule at the binder arm (Z6's C1a, R7.1, R6.8), the members read there given: a fit from another Origin or a
    // value that may write fitted through a region graph row (O-498 Q2 (b): the layer is read by its stated Origin only where nothing else
    // is fitted into it; Revision 8), or a claim or an address past a shared path with an exclusive member.
    private static bool LimitsBinder(LimitMarks marks, ReadOnlySpan<RawMember> members)
        => (marks & (LimitMarks.FittedIntoHolder | LimitMarks.FittedExclusive)) != 0 ||
            ((marks & (LimitMarks.Claimed | LimitMarks.SharedAddress)) != 0 && AnyExclusive(members));

    private static bool AnyExclusive(ReadOnlySpan<RawMember> members)
    {
        foreach (var member in members)
        {
            if (member.Exclusive)
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 5.2.1 condition 3, 15.6.4 item 1 (Z6's R5.10): whether an Origin's members where it is bound keep only shared Loans: answered,
    // a member, none exclusive. The binder reading's Shared step and MayWrite's "no write authority" read this one rule.
    private static bool SharedOnly(bool complete, ReadOnlySpan<RawMember> members) => complete && members.Length > 0 && !AnyExclusive(members);

    // Whether RawStepAt judges a layer where its Origin is bound (BinderStep's arm: a Type argument's, a Closure environment's), the one
    // arm whose members are read by the Origin's binder (BinderReading), not within the walked scope (Z6's R5.1).
    private bool BoundElsewhere(BoundType? written, BoundType? signature)
        => written is not { IsPointer: true } && this.retentionInputs != 0 && !(signature is { IsPointer: true } && this.retentionDeclared.Count > 0);

    // Z6's one reader of the publication's limit marks (R6.2, G147 ruling 67): an atom's (Binding.AtomMarks), each operand of a meet's
    // joined.
    private LimitMarks Marks(BoundOrigin origin)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return this.AtomMarks(origin);
        }

        var joined = LimitMarks.None;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            joined |= this.AtomMarks(origin.Operands[i]);
        }

        return joined;
    }

    // One binder reading per Origin within a retention computation (Z6's R6.2; BinderStep and ContractMembers share its key): the members
    // by the Origin's binder (AtomMembers) and the step they give, Shared where every member keeps only shared Loans (R5.10; SPEC 5.2.1
    // condition 3, 15.6.4 item 1), else the kind's. Kept in binderReadings, cleared with the computation's state (EndRetention; never
    // nested, G74).
    private (int Start, int Count, bool Complete, HoldStep Step) BinderReading(BoundOrigin origin)
    {
        if (this.binderReadings.TryGetValue(origin, out var known))
        {
            return known;
        }

        var start = this.binderReadingMembers.Count;
        var complete = this.AtomMembers(origin, this.binderReadingMembers);
        var shared = SharedOnly(complete, CollectionsMarshal.AsSpan(this.binderReadingMembers)[start..]);

        // A layer the marks limit at this arm (LimitsBinder) has no members here, and steps by its kind, so retention reads it as itself
        // (its pointee) while A5's StoredMembers answers false: the call keeps the located Unsupported_Kd (R6.8 (b2), R7.1). A false
        // answer here would be CallRetention.Limit, another record (OwnershipDepthLimit_Kd) that retains nothing. Removal: the marks'.
        if (complete && LimitsBinder(this.Marks(origin), CollectionsMarshal.AsSpan(this.binderReadingMembers)[start..]))
        {
            this.binderReadingMembers.RemoveRange(start, this.binderReadingMembers.Count - start);
            shared = false;
        }

        var reading = (start, this.binderReadingMembers.Count - start, complete, shared ? HoldStep.Shared : StepOf(origin));
        this.binderReadings.Add(origin, reading);
        return reading;
    }

    // SPEC 5.7 Authority (DECISIONS BU-113): a raw layer judged where its Origin is bound, "never by the caller's actual borrow": a
    // function's Input or Parameter Origin by that function's declared inputs (SignatureStep over TyCtxt.Signature, read in
    // place into retentionBinderInputs), a Type's own slot by its Loan requirement over its declaration's cases; any other (an accessor's
    // or a Closure's own Origin, which has no declared signature here, a body-local Place or inference atom, a set, `static`) by its
    // binder reading (BinderReading: Shared where every member keeps only shared Loans, else its kind; Z6's R5.10).
    private HoldStep BinderStep(BoundOrigin? raw)
    {
        raw = SlotArgument(raw);
        if (raw is { Kind: OriginKind.Parameter, Binder: DeclarationContainerKoto } && NamedSlot(raw, out _) is { } slot)
        {
            return this.RequirementAt(slot, null).Strongest == LoanRequirement.Ref ? HoldStep.Shared : StepOf(raw);
        }

        if (raw is { Kind: OriginKind.Input or OriginKind.Parameter, Binder: FunctionKoto { IsDestructor: false, BoundSymbol: { } function } })
        {
            return this.SignatureStep(raw, this.BinderInputs(function));
        }

        return raw is null ? StepOf(raw) : this.BinderReading(raw).Step;
    }

    // SPEC 5.7 Origins, Authority (DECISIONS BU-113; Z6's B3): the members of an Input or Parameter Origin where it is bound, as BinderStep
    // reads it: a Type's own slot's keepers within Self (each kept referent, exclusive where the slot is `uniq` in some case), a
    // function's Origin within its declared inputs (BinderInputs); none where the slot argument is no Origin a reader reads (StepOf);
    // false for any other binder (an accessor's or a Closure's own Origin, which has no declared signature here).
    private bool BinderMembers(BoundOrigin raw, List<RawMember> members)
    {
        var origin = SlotArgument(raw)!;
        if (origin is { Kind: OriginKind.Parameter, Binder: DeclarationContainerKoto { BoundSymbol: { Schema: not null } declaration } })
        {
            var self = this.SelfType(declaration);
            return this.RawMembers(origin, new ReadOnlySpan<BoundType>(in self), members);
        }

        return origin is { Kind: OriginKind.Input or OriginKind.Parameter, Binder: FunctionKoto { IsDestructor: false, BoundSymbol: { } function } }
            ? this.RawMembers(origin, this.BinderInputs(function), members) : StepOf(origin) != HoldStep.Exclusive;
    }

    // A function's declared inputs (TyCtxt.Signature), read in place into retentionBinderInputs: the one reading BinderStep and BinderMembers
    // take of where its Origins are bound.
    private ReadOnlySpan<BoundType> BinderInputs(BindingSymbol function)
    {
        var inputs = this.retentionBinderInputs;
        inputs.Clear();
        _ = this.types.Signature(function, null, -1, out var count);
        for (var i = 0; i < count; i++)
        {
            if (this.types.Signature(function, null, i, out _) is { } input)
            {
                inputs.Add(input);
            }
        }

        return CollectionsMarshal.AsSpan(inputs);
    }

    // SPEC 5.2.1 condition 3 (the owner's 「Loan 要求で上限 (推奨)」; design-final §15): the step of a raw layer an instance stores (`layer`,
    // in its kept part), capped by the layer its declaration states there (`declared`), whose slot the substitution replaces: Shared when
    // the declaration states the layer with a slot whose Loan requirement is `ref` in every case the holding instance (`holder`) selects
    // (SPEC 15.3.5, DECISIONS BU-44), since a pointer read from it has read authority only; any other is the kind of its Origin under the
    // instance's substitution (`self` the holder's own step, an Unbound slot argument Unstated). A layer a Type argument supplies
    // (`declared` null) is no layer the declaration states, so no cap reads it: it has the kind of the Origin the argument states, as any
    // layer no declaration states (a pair slot's argument states none, TyCtxt.PairBinding). StoredStep and the hold walk (Held) read this
    // one rule.
    private HoldStep DeclaredStep(BoundType layer, BoundType? declared, BoundType? holder)
        => NamedSlot(declared?.RawOrigin, out _) is { } slot && this.RequirementAt(slot, holder).Strongest == LoanRequirement.Ref
            ? HoldStep.Shared : StepOf(layer.RawOrigin);

    // SignatureStep's reading of an input Type: the authority the statements of `origin` in it can keep, joined with `kept` (HoldStep's order:
    // Unstated while none states it, Shared while each keeps only shared Loans, else Exclusive). A statement is a layer's outer Origin or a
    // slot argument; a raw layer's Origin states no borrow.
    private HoldStep Statements(BoundType type, BoundOrigin origin, HoldStep kept)
    {
        if (!type.CarriesOriginOrSlot || kept == HoldStep.Exclusive)
        {
            return kept;
        }

        var pair = TryPairLayer(type, out _, out _);
        if (Names(pair ? this.OuterOrigin(type) : type.Origin, origin))
        {
            var shared = type.Kind == BoundTypeKind.Slice || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef } ||
                (pair && this.LayerAdmitted(type) is not SemanticsMask.None and var admitted && (admitted & ~(SemanticsMask.Ref | SemanticsMask.ObjRef)) == 0);
            kept = Max(kept, shared ? HoldStep.Shared : HoldStep.Exclusive);
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (Names(type.OriginArguments[i], origin))
            {
                var shared = type.Symbol?.Schema is { } schema && i < schema.Origins.Count &&
                    this.RequirementAt(schema.Origins[i], type).Strongest == LoanRequirement.Ref;
                kept = Max(kept, shared ? HoldStep.Shared : HoldStep.Exclusive);
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            kept = this.Statements(type.Components[i], origin, kept);
        }

        return kept;

        static HoldStep Max(HoldStep a, HoldStep b) => a < b ? b : a;
    }

    // The raw layer an instance stores at a struct's Field `part`, or an enum's Case `part` whose first position selects the payload's
    // component, then the inline positions (a Tuple's component, a fixed array's element), in the instance's kept part (Instance: its
    // substitution with the raw Origins it gives) where its declaration writes the part, and as `declared` the raw layer its declaration
    // states there (WrittenAt): null where a Type argument supplies the layer, the declaration stating another Type (a Type parameter, a
    // pair layer) at the path or above it. A path whose kept part is no raw layer, a Case's that did not substitute (null; DECISIONS
    // BU-37) included, is a broken contract, as is a `during self` layer the declaration does not state there: `self` binds Holder only
    // where WrittenAt is non-null, so no layer below that region steps Own (DECISIONS BU-58).
    private BoundType StoredLayer(BoundType instance, int part, ReadOnlySpan<int> positions, out BoundType? declared)
    {
        var written = WrittenAt(instance, part, positions, out var writes);
        var stored = writes ? this.Instance(instance).Parts[part] : null;
        for (var i = 0; i < positions.Length && stored is not null; i++)
        {
            stored = InlinePart(stored, positions[i]);
        }

        declared = written is { IsPointer: true } ? written : null;
        return stored is { IsPointer: true } && (declared is not null || this.DeclaredStep(stored!, null, instance) != HoldStep.Own) ? stored!
            : throw new InvalidOperationException($"No raw layer, or an unstated `self` one, is stored at part {part} of `{instance}` (InternalInvariant).");
    }
}
