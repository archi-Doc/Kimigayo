// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A Closure's call receiver (SPEC 7.7.3, 7.7.2): the minimum receiver a call of the Closure needs, classified from the uses of its
// captures in its bound body.
// - Responsibility: ClosureEffects, one reusable visitor: a capture or a stored part of it that its use moves (the one place-use
//   rule's Consuming, Binding.UseOf), or an outer environment binding a nested Closure's entry moves, makes the call Consuming; a
//   Place reached from a capture (the capture itself or a Place its path reaches through NextPathStep's steps: a part of it, a
//   captured referent, a raw pointer's referent) whose use needs it exclusively (NeedsExclusively, by the same rule over its
//   consumer's takes; past a raw step also a Take or an in-place destruction, and the path continues through a pointer derived from
//   a reached one, DerivesPointer: SPEC 7.7.3's environment rule), or a nested entry that Reborrows or exclusively borrows an outer
//   environment binding, makes it Exclusive; every other use borrows (Shared). An acquisition of the binding itself
//   by value without an operator (TakenByCase: as the Adjust rule takes it from its consumer, a discarded Place included, or a
//   nested bare entry) that Reborrows in some admitted cases makes it Exclusive in exactly those (SPEC 8.14): the capture is marked
//   (BoundCapture.BareReborrows); every other use counts in every case (CaseFreeReceiver). A use of such a capture that no consumer
//   takes is published (Unlisted) where it could leave a case Shared. A use a failure explains (SPEC 23.1.2: its own, or its
//   consumer's failure or dependency outside a bare acquisition position) neither marks its capture nor is published.
// - Inputs: the Closure's bound body (each node's bound symbol, Type, call, value call and conversion), the adaptations Binding
//   recorded, a nested Closure's published Storage; the Adjust rule (AdjustChildren, ChildAdjust, Discarded, Covers, OpOf, UseOf),
//   IsBareAcquisitionPosition, HasFailureCauses and BareReborrows.
// - Outputs: BoundClosure's receiver facts: Receiver, the strongest over every case, which every reader of a Closure's call
//   receiver reads (value calls, candidate selection and Callable proofs, effects and the MIR builder);
//   CaseFreeReceiver and the captures' BareReborrows, from which a view of one case reads that case's receiver; Classified;
//   ExclusiveUse and ExclusiveReborrow, the Exclusive cause a write record names (Binding.Diagnostics.cs); Unlisted, which
//   BindConcreteClosure fails as the rule's net, a located Unsupported_Kd (the owner, 2026-10-25; DECISIONS R4-M145); Uncontracted,
//   the calls of a foreign import passed a pointer reached from a capture, which it fails alike (DECISIONS R7-Y2).
// - Invariants: Classify runs once per binding of a Closure that bound its body, right after it, from the facts BindClosure and
//   BindConcreteClosure reset (Shared, no mark, no cause, not Classified); only this binding's captures are classified; the
//   case-free receiver only strengthens, Receiver is derived from the facts once at the end and the first exclusive use while the
//   call is not Consuming is kept; a nested function's body is never visited, so one Closure is classified at a time.
// - Prohibitions: no record or failure; no other component classifies a Closure's receiver.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private ClosureEffects? closureEffects;

    /// <summary>Gets the children's takes ClosureEffects recorded (once per consumer and selection), which a scaling check reads.</summary>
    internal long ClosureTakeWork => this.closureEffects?.TakeWork ?? 0;

    // One reusable visitor classifies each closure's receiver from the uses of its captures (SPEC 7.7.3); it never visits a
    // nested function's body, so one closure is classified at a time.
    private sealed class ClosureEffects(Binding binding) : KotoVisitor
    {
        // Each consumer's takes of its children by selection, and the consumers recorded, in this Classify (TakesOf); kept across Classify
        // calls, so a warm classification reuses their storage.
        private readonly Dictionary<(Koto Child, bool Exclusive), ConsumedTakes> takes = new();
        private readonly HashSet<(Koto Consumer, bool Exclusive)> walks = new();

        // Each node's Op read in this Classify (OpOf): a call's Op reads its N argument operations, so every capture use of its consumer
        // reads it once.
        private readonly Dictionary<Koto, HirOp> ops = new();
        private FunctionKoto function = null!;
        private BoundClosure plan = null!;

        // The children's takes recorded, over every Classify: a scaling check reads it (ClosureTakeWork).
        public long TakeWork { get; private set; }

        // The uses of a capture that Reborrows in some admitted case that no consumer takes (TakenByCase is null), with the capture's
        // index; after Classify, only those the receiver facts leave at Shared in a case (DECISIONS R4-M145).
        public List<(Koto Use, int Capture)> Unlisted { get; } = new();

        // The calls of a foreign import that a pointer reached from a capture is passed to, whose contract's writes no published fact states
        // (ContractWritesThrough; DECISIONS R7-Y2).
        public List<Koto> Uncontracted { get; } = new();

        public void Classify(FunctionKoto function, BoundClosure plan)
        {
            this.takes.Clear();
            this.walks.Clear();
            this.ops.Clear();
            this.function = function;
            this.plan = plan;
            this.Unlisted.Clear();
            this.Uncontracted.Clear();
            if ((function.Body as Koto ?? function.ExpressionBody) is { } body)
            {
                this.Visit(body);
            }

            // SPEC 7.7.3: the strongest requirement over every admitted case.
            var receiver = plan.CaseFreeReceiver;
            for (var i = 0; receiver == SemanticsKind.Ref && i < plan.Storage.Count; i++)
            {
                receiver = plan.Storage[i].BareReborrows ? SemanticsKind.Uniq : receiver;
            }

            plan.Receiver = receiver;
            plan.Classified = true;

            // The rule's net (the owner, 2026-10-25): an untaken use matters only where its capture is unmarked and the case-free
            // receiver Shared, so a case may be Shared where SPEC 7.7.3 needs Exclusive.
            var kept = 0;
            for (var i = 0; i < this.Unlisted.Count; i++)
            {
                if (plan.CaseFreeReceiver == SemanticsKind.Ref && !plan.Storage[this.Unlisted[i].Capture].BareReborrows)
                {
                    this.Unlisted[kept++] = this.Unlisted[i];
                }
            }

            this.Unlisted.RemoveRange(kept, this.Unlisted.Count - kept);
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto nested)
            {
                if (nested.ClosureDefinition is { } child)
                {
                    this.VisitEntries(nested, child);
                }

                return;
            }

            if (node.BoundSymbol is { Kind: BindingSymbolKind.Capture } symbol && this.CaptureOf(symbol) is >= 0 and var at)
            {
                var use = node;
                while (use.Parent is ParenthesizedKoto ||
                    (use.Parent is IndexKoto index && ReferenceEquals(index.Left, use) && use.BoundType?.Kind == BoundTypeKind.FixedArray) ||
                    (use.Parent is MemberAccessKoto part && ReferenceEquals(part.Left, use) &&
                        (use.BoundType?.Kind == BoundTypeKind.Tuple || (AdtDef.IsStruct(use.BoundType) && part.BoundSymbol?.Property?.IsStored == true && !IsGetterResult(part)))))
                {
                    // Reading or updating a captured stored part obtains that part's authority, not a whole-value Move: the
                    // Consuming need is the take of the stored part a use takes.
                    use = use.Parent;
                }

                var need = this.UsesExclusively(node, use);
                if (need.Use is PlaceUse.ByCase or PlaceUse.Exclusive)
                {
                    if (need.Use == PlaceUse.ByCase)
                    {
                        this.plan.Storage[at] = this.plan.Storage[at] with { BareReborrows = true };
                    }
                    else if (this.plan.CaseFreeReceiver != SemanticsKind.Owner)
                    {
                        this.plan.CaseFreeReceiver = SemanticsKind.Uniq;
                    }

                    if (this.plan.CaseFreeReceiver != SemanticsKind.Owner && this.plan.ExclusiveUse is null)
                    {
                        this.plan.ExclusiveUse = node;
                        this.plan.ExclusiveReborrow = binding.adaptations.TryGetValue(use, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow;
                    }
                }
                else if (need.Use == PlaceUse.Consuming)
                {
                    // SPEC 7.7.3, 13.5.3: transferring a capture out of the environment makes the call Consuming; a Consuming call of
                    // a captured Copy Closure Copies it. Ownership rejects a Move out of a capture in a Shared or Exclusive call.
                    this.plan.CaseFreeReceiver = SemanticsKind.Owner;
                }

                if (!need.Listed)
                {
                    this.Unlisted.Add((node, at)); // No consumer takes this use, so no rule decides whether it Reborrows by case.
                }
            }

            node.VisitChildren(this);
        }

        // The operation by which a call takes the Place as an argument, or null.
        private static BoundArgumentOperation? ArgumentOf(ReadOnlySpan<BoundArgumentOperation> arguments, Koto place)
        {
            foreach (ref readonly var argument in arguments)
            {
                if (ReferenceEquals(argument.Source, place))
                {
                    return argument;
                }
            }

            return null;
        }

        // SPEC 5.2.1 condition 3's derived pointers, a closed list (5.3, 5.4): a displacement `p + n` or `p - n`, a conversion between raw
        // pointer Types, an Origin-stating or Origin-dropping conversion or fit, or an identity conversion, each keeping the operand's
        // provenance. No other form derives: a `usize` round trip has none (5.5), and a pointer a call with a body returns is not derived
        // from its arguments (the owner's O-76 (b) (i)). R7-Y2c's copies through the body's locals read this list.
        private static bool DerivesPointer(Koto place) => ReferenceTypes.IsPointer(place.BoundType) && place.Parent switch
        {
            BinaryKoto { Akind: KotoKind.Plus or KotoKind.Minus } displacement => ReferenceEquals(displacement.Left, place) && ReferenceTypes.IsPointer(displacement.BoundType),
            ConversionKoto { ConversionBinding: ConversionBinding.Pointer or ConversionBinding.Identity } conversion =>
                ReferenceEquals(conversion.Left, place) && ReferenceTypes.IsPointer(conversion.BoundType),
            _ => false,
        };

        // SPEC 7.7.2, 7.7.3: a nested Closure's entries of this binding's captures, each taken as the one place-use rule's Take takes its
        // acquisition (BoundCapture.Adjust) at the nested Closure: moving an outer environment value is Consuming; Reborrowing it or
        // borrowing its slot exclusively needs exclusive access to the outer environment, in the cases where it Reborrows for a bare entry.
        private void VisitEntries(FunctionKoto nested, BoundClosure child)
        {
            // Indexed over the storage list: enumerating the read-only interface boxes its enumerator on every pass.
            for (var i = 0; i < child.Storage.Count; i++)
            {
                var capture = child.Storage[i];
                if (this.CaptureOf(capture.Source) is not (>= 0 and var outer))
                {
                    continue;
                }

                var use = binding.Take(capture.Source.Type, nested, capture.Adjust);
                if (use == PlaceUse.Consuming)
                {
                    this.plan.CaseFreeReceiver = SemanticsKind.Owner;
                    continue;
                }

                if (use == PlaceUse.Shared)
                {
                    continue;
                }

                if (use == PlaceUse.ByCase)
                {
                    this.plan.Storage[outer] = this.plan.Storage[outer] with { BareReborrows = true };
                }
                else if (this.plan.CaseFreeReceiver != SemanticsKind.Owner)
                {
                    this.plan.CaseFreeReceiver = SemanticsKind.Uniq;
                }

                if (this.plan.CaseFreeReceiver != SemanticsKind.Owner)
                {
                    this.plan.ExclusiveUse ??= nested;
                }
            }
        }

        // SPEC 7.7.3, 7.7.2: whether the body uses a Place reached from a capture (the capture itself or a Place its path reaches, one
        // NextPathStep at a time: a part of it through Fields or elements, a captured referent through a follow, a raw pointer's
        // referent, `*p` as `p[0]`, SPEC 5.2) exclusively. Such a body uses its environment, or what it reaches, exclusively, so the call is
        // Exclusive, by case only where that use is an acquisition of the capture itself by value that Reborrows in some admitted cases,
        // which the capture's case Type tells. Such a Reborrow of a Place reached through the capture takes that Place's cases, which
        // the capture's do not tell, so it counts in every case: the strongest receiver (PLAN G13). A Move of the capture or of a stored
        // part of it (owned, Visit's) transfers it out of the environment (Consuming). Whether a rule decides the need by case (Listed) is
        // the capture's own (NeedsExclusively).
        private Need UsesExclusively(Koto capture, Koto owned)
        {
            var listed = true;
            var raw = false;
            for (var place = capture; ; place = place.Parent!)
            {
                var need = this.NeedsExclusively(place, raw);
                listed = ReferenceEquals(place, capture) ? need.Listed : listed;
                if (need.Use is PlaceUse.ByCase or PlaceUse.Exclusive)
                {
                    return new(need.Use == PlaceUse.ByCase && ReferenceEquals(KotoHelper.UnwrapParentheses(place), capture) ? PlaceUse.ByCase : PlaceUse.Exclusive, listed);
                }

                if (need.Use == PlaceUse.Consuming && ReferenceEquals(place, owned))
                {
                    return new(PlaceUse.Consuming, listed);
                }

                // SPEC 7.7.3: past a raw pointer's pointee the environment's write authority is what an access needs. A pointer derived
                // from an environment pointer (DerivesPointer) is that pointer, and a pointer read through a raw Place the environment
                // reaches is read from the environment (the owner's O-76 (c) (i)), so the walk continues past a raw step.
                var step = NextPathStep(place.Parent!, out var next);
                raw |= step == PathStep.RawFollow && ReferenceEquals(next, place);
                if (!(IsGroup(place.Parent) || ReferenceEquals(next, place) || DerivesPointer(place)))
                {
                    return new(PlaceUse.Shared, listed);
                }
            }
        }

        // What the use of a Place needs (SPEC 7.7.3, 3.5.1, UseOf): exclusively, past a raw step (raw) also by a Take or an in-place
        // destruction (SPEC 5.2.1 condition 3, 5.2.3); by case when only a pair-case acquisition that Reborrows in some admitted cases needs
        // it (SPEC 8.14), which TakenByCase decides for such a Place; else its value moved (Consuming) or nothing (Shared).
        private Need NeedsExclusively(Koto place, bool raw)
        {
            var use = this.UseOf(place);
            if (use == PlaceUse.Exclusive || (raw && use == PlaceUse.Consuming) || this.ContractWritesThrough(place))
            {
                return new(PlaceUse.Exclusive, true);
            }

            var byCase = place.BoundType is { } bare && binding.BareReborrows(bare, place) ? this.TakenByCase(place) : false;
            return new(byCase == true ? PlaceUse.ByCase : use == PlaceUse.Consuming ? PlaceUse.Consuming : PlaceUse.Shared, byCase is not null);
        }

        // SPEC 5.2, 7.7.3 (the owner's O-21 (ii)): whether a pointer reached from a capture is passed to a call whose operation contract
        // performs an access that needs write capability through it (IntrinsicDisposition.WritesThrough), the body's own access. A foreign
        // import's contract is prose the compiler cannot read (SPEC 22.6), so its call is kept as Uncontracted, a located Unsupported_Kd.
        private bool ContractWritesThrough(Koto place)
        {
            if (!ReferenceTypes.IsPointer(place.BoundType) || place.Parent is not InvocationKoto { BoundCall: { } call } invocation ||
                ArgumentOf(call.ArgumentOperations, place) is not { } argument)
            {
                return false;
            }

            if (call.Target.Declaration is FunctionKoto declaration && declaration.FindAttribute(AttributeKind.LibraryImport) is not null)
            {
                if (!this.Uncontracted.Contains(invocation))
                {
                    this.Uncontracted.Add(invocation);
                }

                return false;
            }

            return IntrinsicDisposition.WritesThrough(call.Target.CompilerFunction, argument.ParameterIndex);
        }

        // What the use of a Place reached from a capture needs, as the one place-use rule decides it (Binding.UseOf) over its consumer's
        // takes under the shared selection (TakesOf: an exclusive use of a user index's element is found above it, SPEC 4.7.1). A grouped
        // Place is taken as its group.
        private PlaceUse UseOf(Koto place)
        {
            if (IsGroup(place.Parent))
            {
                return PlaceUse.Shared;
            }

            var takes = this.TakesOf(place, exclusive: false) ?? default;
            return binding.UseOf(place, takes.Last, takes.Earlier, place.Parent, this.OpOf(place.Parent!));
        }

        // A node's evaluated Op (Binding.OpOf), read once per Classify.
        private HirOp OpOf(Koto node)
        {
            if (!this.ops.TryGetValue(node, out var op))
            {
                this.ops[node] = op = binding.OpOf(node);
            }

            return op;
        }

        // SPEC 3.5, 8.14, 14.2.2, 23.1.2 (DECISIONS R4-M145): whether a Place is taken by case (Take's ByCase, a pair-case acquisition that
        // Reborrows in some admitted case) as the Adjust rule takes it (TakesOf), its groups taken through: false where its own failure
        // explains it; null where its consumer takes no such child (the net) or a user index's two selections take it differently (SPEC 4.7.1).
        private bool? TakenByCase(Koto place)
        {
            if (Rejected(place))
            {
                return false;
            }

            var shared = this.TakesOf(place, exclusive: false)?.ByCase(binding, place);
            return shared == this.TakesOf(place, exclusive: true)?.ByCase(binding, place) ? shared : null;
        }

        // SPEC 3.5, 14.2.2, 23.1.2: a Place's takes, its groups taken through, under one capability of its consumer's own use: in Discard
        // Context, as its container discards it (Binding.Discarded, the plan-row Adjust the MIR builder reads); else as its consumer takes
        // it, the nearest ancestor its parent does not cover (Binding.Covers), the Closure under classification taken as a Closure. A
        // consumer whose failure or dependency explains its inputs (HasFailureCauses) leaves only a position that acquires by bare
        // acquisition by itself its own take (IsBareAcquisitionPosition); one the HIR publisher's walk takes no child of takes none.
        // One consumer's takes are recorded once per Classify and selection (TakeRecord, every child's two rows), so a consumer with N capture
        // uses is walked once, not once per use; null where it takes no such child.
        private ConsumedTakes? TakesOf(Koto place, bool exclusive)
        {
            var target = Grouped(place);
            if (!IsValueContext(target))
            {
                return new(binding.Discarded(place), default);
            }

            var consumer = target.Parent!;
            while (consumer.Parent is { } outer && Covers(outer, consumer))
            {
                consumer = outer;
            }

            var closure = ReferenceEquals(consumer, this.function);
            if (!closure && binding.HasFailureCauses(consumer))
            {
                return new(IsBareAcquisitionPosition(target) ? binding.ChildAdjust(target, HirAdaptation.None, UseKind.Acquisition, claimed: false) : default, default);
            }

            var op = closure ? HirOp.Closure : this.OpOf(consumer);
            if (op is HirOp.Unset or HirOp.Error or HirOp.Syntax)
            {
                return null; // The HIR publisher's walk takes no child of such a consumer.
            }

            if (this.walks.Add((consumer, exclusive)))
            {
                var record = new TakeRecord(binding, this.takes, exclusive);
                binding.AdjustChildren(consumer, op, exclusive, ref record);
                this.TakeWork += record.Count;
            }

            return this.takes.TryGetValue((target, exclusive), out var takes) ? takes : null;
        }

        // The index of the capture of this binding whose environment binding a use names, or -1 (SPEC 7.7.3: the resolved operations of
        // the body this binding bound).
        private int CaptureOf(BindingSymbol environment)
        {
            for (var i = 0; i < this.plan.Storage.Count; i++)
            {
                if (ReferenceEquals(this.plan.Storage[i].Environment, environment))
                {
                    return i;
                }
            }

            return -1;
        }

        // What a use of a Place needs of the environment (SPEC 7.7.3, 8.14): exclusive access (Exclusive), or only in the admitted cases
        // where its acquisition by value Reborrows (ByCase), its value moved out (Consuming), or none of these (Shared); and whether a rule
        // decides its acquisition by case (Listed: not where no consumer takes it, TakenByCase).
        private readonly record struct Need(PlaceUse Use, bool Listed);

        // One child's takes by its consumer, as the Adjust rule gives them and the HIR publisher writes them: the last, the column's, and
        // the earlier call's that a later one replaces by another (EarlierAdjust, the plan row's).
        private readonly record struct ConsumedTakes(HirAdjust Last, HirAdjust Earlier)
        {
            // Whether either take is a pair-case acquisition of the child that Reborrows in some admitted case (Binding.Take's ByCase).
            public bool ByCase(Binding binding, Koto child) =>
                binding.Take(child.BoundType, child, this.Last) == PlaceUse.ByCase || binding.Take(child.BoundType, child, this.Earlier) == PlaceUse.ByCase;
        }

        // The record of one consumer's takes under one selection (TakesOf), every child's as the publisher's RowUses writes them: each
        // completed by the claim of its two rows so far (Claimed, DECISIONS R7-M8).
        private struct TakeRecord(Binding binding, Dictionary<(Koto Child, bool Exclusive), ConsumedTakes> taken, bool exclusive) : IHirUses
        {
            private int call = -1;

            // The takes recorded (ClosureTakeWork).
            public int Count { get; private set; }

            public void Call(int ordinal) => this.call = ordinal;

            public void Use(Koto? child, HirAdaptation adaptation, UseKind kind)
            {
                if (child is not null)
                {
                    this.FixAdjust(child, binding.ChildAdjust(child, adaptation, kind, this.Claimed(child)), default);
                }
            }

            public void Use(Koto? child, in BoundArgumentOperation operation)
            {
                if (child is not null)
                {
                    this.FixAdjust(child, binding.ChildAdjust(child, operation, this.Claimed(child)), default);
                }
            }

            public void FixAdjust(Koto? child, HirAdjust adjust, HirPlace place)
            {
                if (child is not null)
                {
                    this.Count++;
                    taken.TryGetValue((child, exclusive), out var takes);
                    taken[(child, exclusive)] = new(adjust, this.call >= 0 ? EarlierAdjust(this.call, takes.Last, adjust) : takes.Earlier);
                }
            }

            private readonly bool Claimed(Koto child) => taken.TryGetValue((child, exclusive), out var takes) && binding.Claimed(child, takes.Last, takes.Earlier);
        }
    }
}
