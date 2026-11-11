// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A Closure's call receiver (SPEC 7.7.3, 7.7.2): the minimum receiver a call of the Closure needs, classified from the uses of its
// captures in its bound body.
// - Responsibility: ClosureEffects, one reusable visitor: a capture or a stored part of it that its use moves (the one place-use
//   rule's Consuming, Binding.UseOf), or an outer environment binding a nested Closure's entry moves, makes the call Consuming; a
//   Place reached from a capture (the capture itself or a Place its path reaches through NextPathStep's steps: a part of it, a
//   captured referent, a raw pointer's referent) whose use needs it exclusively (NeedsExclusively, by the same rule over its
//   consumer's takes; past a raw step also a Take or an in-place destruction, and the path continues through a pointer derived from
//   a reached one, Below: SPEC 7.7.3's environment rule; a use of a pointer-typed body local that every definition derives from an
//   environment pointer starts the walk past its own Place, Derive, R7-Y2c), or a nested entry that Reborrows or exclusively borrows an outer
//   environment binding, makes it Exclusive; every other use borrows (Shared). An acquisition of the binding itself
//   by value without an operator (TakenByCase: as the Adjust rule takes it from its consumer, a discarded Place included, or a
//   nested bare entry) that Reborrows in some admitted cases makes it Exclusive in exactly those (SPEC 8.14): the capture is marked
//   (BoundCapture.BareReborrows); every other use counts in every case (CaseFreeReceiver). A use of such a capture that no consumer
//   takes is published (Unlisted) where it could leave a case Shared. A use a failure explains (SPEC 23.1.2: its own, or its
//   consumer's failure or dependency outside a bare acquisition position) neither marks its capture nor is published.
// - Inputs: the Closure's bound body (each node's bound symbol, Type, call, value call and conversion), the adaptations Binding
//   recorded, a nested Closure's published Storage; the Adjust rule (AdjustChildren, ChildAdjust, Discarded, Covers, OpOf, UseOf),
//   IsBareAcquisitionPosition, HasFailureCauses and BareReborrows; each node's value identity (SourceRef), the key of what a Classify
//   knows of it.
// - Outputs: BoundClosure's receiver facts: Receiver, the strongest over every case, which every reader of a Closure's call
//   receiver reads (value calls, candidate selection and Callable proofs, effects and the MIR builder);
//   CaseFreeReceiver and the captures' BareReborrows, from which a view of one case reads that case's receiver; Classified;
//   ExclusiveUse and ExclusiveReborrow, the Exclusive cause a write record names (Binding.Diagnostics.cs); Unlisted, which
//   BindConcreteClosure fails as the rule's net, a located Unsupported_Kd (the owner, 2026-10-25; DECISIONS R4-M145); Undecided,
//   the uses whose need no fact decides (a foreign import's call passed a pointer reached from a capture, DECISIONS R7-Y2; a write through
//   a body local only some definitions derive from an environment pointer, R7-Y2c), which it fails alike.
// - Invariants: Classify runs once per binding of a Closure that bound its body, right after it, from the facts BindClosure and
//   BindConcreteClosure reset (Shared, no mark, no cause, not Classified); only this binding's captures are classified; the
//   case-free receiver only strengthens, Receiver is derived from the facts once at the end and the first exclusive use while the
//   call is not Consuming is kept; a nested function's body is never visited, so one Closure is classified at a time. What a
//   Classify knows of a node (Facts: its Op, its children's takes recorded, its own takes) is that Classify's: the store is emptied at
//   its start, holds one row per node met (a consumer, a child it takes, a parent whose Op is read) and is never sized by the module.
//   A row's key is its node's SourceRef.Of, which numbers no node here and moves none to another table: every node met is one the
//   Adjust rule reads in a bound body, so its module's table already numbers it (FactsOf, TakeRecord.Row).
// - Prohibitions: no record or failure; no other component classifies a Closure's receiver.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private ClosureEffects? closureEffects;

    /// <summary>Gets the children's takes ClosureEffects recorded (once per consumer and selection) and the definitions it read, which a
    /// scaling check reads.</summary>
    internal long ClosureWork => this.closureEffects?.Work ?? 0;

    // One reusable visitor classifies each closure's receiver from the uses of its captures (SPEC 7.7.3); it never visits a
    // nested function's body, so one closure is classified at a time.
    private sealed class ClosureEffects(Binding binding) : KotoVisitor
    {
        // What this Classify knows of each node it met (Facts), by the node's value identity; kept across Classify calls, so a warm
        // classification reuses its storage, which the nodes met size.
        private readonly Dictionary<SourceRef, Facts> met = new();

        // SPEC 5.2.1 condition 3, 7.7.3 (R7-Y2c): each definition of a pointer-typed body local (its initializer or assigned value) by the
        // root it is reached from (RootOf); the uses of such locals; and what each local's definitions hold (Derive).
        private readonly List<(BindingSymbol Local, Koto Root)> definitions = new();
        private readonly List<Koto> localUses = new();
        private readonly Dictionary<BindingSymbol, Holds> holds = new();
        private FunctionKoto function = null!;
        private BoundClosure plan = null!;

        // The children's takes recorded and the definitions Derive read, over every Classify: a scaling check reads it (ClosureWork).
        public long Work { get; private set; }

        // The uses of a capture that Reborrows in some admitted case that no consumer takes (TakenByCase is null), with the capture's
        // index; after Classify, only those the receiver facts leave at Shared in a case (DECISIONS R4-M145).
        public List<(Koto Use, int Capture)> Unlisted { get; } = new();

        // The uses whose need no published fact decides, each a located Unsupported_Kd: a call of a foreign import that a pointer reached
        // from a capture is passed to, whose contract's writes are prose (ContractWritesThrough; DECISIONS R7-Y2); a write-capability access
        // through a body local that only some definitions derive from an environment pointer, where no flow fact states which one reaches
        // the access (R7-Y2c).
        public List<Koto> Undecided { get; } = new();

        public void Classify(FunctionKoto function, BoundClosure plan)
        {
            this.met.Clear();
            this.definitions.Clear();
            this.localUses.Clear();
            this.holds.Clear();
            this.function = function;
            this.plan = plan;
            this.Unlisted.Clear();
            this.Undecided.Clear();
            if ((function.Body as Koto ?? function.ExpressionBody) is { } body)
            {
                this.Visit(body);
            }

            // SPEC 5.2.1 condition 3, 7.7.3: a use of a body local holding a pointer derived from an environment pointer starts the walk as
            // a capture's use does; its own Place is the body's (UsesExclusively).
            this.Derive();
            foreach (var use in this.localUses)
            {
                if (this.holds.TryGetValue(use.BoundSymbol!, out var held) && held.HasFlag(Holds.Environment) &&
                    this.UsesExclusively(use, null).Use == PlaceUse.Exclusive)
                {
                    if (held != Holds.Environment)
                    {
                        this.Undecided.Add(use); // Only some definitions hold one: no flow fact states which reaches this use.
                    }
                    else if (this.plan.CaseFreeReceiver != SemanticsKind.Owner)
                    {
                        this.plan.CaseFreeReceiver = SemanticsKind.Uniq;
                        this.plan.ExclusiveUse ??= use;
                    }
                }
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
                        // The Reborrow cause holds in every case only for a take that is no by-case one: a pair layer's Reborrow is its
                        // exclusive cases' (PLAN G254), which the general cause covers without naming a case.
                        this.plan.ExclusiveUse = node;
                        this.plan.ExclusiveReborrow = need.Use == PlaceUse.Exclusive && binding.adaptations.TryGetValue(use, out var adaptation) &&
                            adaptation.Kind == ExpectedAdaptationKind.Reborrow;
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
            else if (node.BoundSymbol is { Kind: BindingSymbolKind.Local } local && ReferenceTypes.IsPointer(local.Type))
            {
                this.localUses.Add(node);
            }

            // A pointer-typed local's definitions (Derive): its initializer and each assigned value. A compound displacement `q += n` keeps
            // the local's own provenance (SPEC 5.3), no definition.
            if (node is VariableKoto { InitializerKoto: { } value } variable && variable.NameKoto.BoundSymbol is { Kind: BindingSymbolKind.Local } declared &&
                ReferenceTypes.IsPointer(declared.Type))
            {
                this.definitions.Add((declared, RootOf(value)));
                this.holds.TryAdd(declared, Holds.None);
            }
            else if (node is BinaryKoto { Akind: KotoKind.Equals } assignment &&
                KotoHelper.UnwrapParentheses(assignment.Left).BoundSymbol is { Kind: BindingSymbolKind.Local } assigned &&
                ReferenceTypes.IsPointer(assigned.Type))
            {
                this.definitions.Add((assigned, RootOf(assignment.Right)));
                this.holds.TryAdd(assigned, Holds.None);
            }

            node.VisitChildren(this);
        }

        // The root a value is reached from, Below's steps descended: a call's result, a literal or a binding (SPEC 5.2.1 condition 3).
        private static Koto RootOf(Koto value)
        {
            while (Below(value, out _) is { } below)
            {
                value = below;
            }

            return value;
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

        // One step of the walk from a node toward what it is reached from (UsesExclusively climbs it): a group's operand, the path's next
        // Place (NextPathStep, step), or a derived pointer's operand, SPEC 5.2.1 condition 3's closed list (5.3, 5.4): a displacement
        // `p + n` or `p - n`, a conversion between raw pointer Types, an Origin-stating or Origin-dropping conversion or fit, or an identity
        // conversion, each keeping the operand's provenance; null where none. No other form derives: a `usize` round trip has none (5.5), and
        // a pointer a call with a body returns is not derived from its arguments (the owner's O-76 (b) (i)).
        private static Koto? Below(Koto node, out PathStep step)
        {
            step = NextPathStep(node, out var next);
            return node switch
            {
                ParenthesizedKoto group => group.Operand,
                LabeledKoto label => label.Target,
                _ when !ReferenceEquals(next, node) => next,
                BinaryKoto { Akind: KotoKind.Plus or KotoKind.Minus } displacement when ReferenceTypes.IsPointer(displacement.BoundType) &&
                    ReferenceTypes.IsPointer(displacement.Left.BoundType) => displacement.Left,
                ConversionKoto { ConversionBinding: ConversionBinding.Pointer or ConversionBinding.Identity } conversion when
                    ReferenceTypes.IsPointer(conversion.BoundType) && ReferenceTypes.IsPointer(conversion.Left.BoundType) => conversion.Left,
                _ => null,
            };
        }

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
        // the capture's own (NeedsExclusively). From a body local holding an environment pointer (owned null, R7-Y2c) the walk starts the
        // same way, but the local's Place is the body's: before a raw step only an operation contract's write through the pointer counts.
        private Need UsesExclusively(Koto capture, Koto? owned)
        {
            var listed = true;
            var raw = false;
            for (var place = capture; ; place = place.Parent!)
            {
                var need = owned is null && !raw
                    ? new Need(this.ContractWritesThrough(place) ? PlaceUse.Exclusive : PlaceUse.Shared, true)
                    : this.NeedsExclusively(place, raw);
                listed = ReferenceEquals(place, capture) ? need.Listed : listed;
                if (need.Use is PlaceUse.ByCase or PlaceUse.Exclusive)
                {
                    var byCase = need.Use == PlaceUse.ByCase && ReferenceEquals(KotoHelper.UnwrapParentheses(place), capture);
                    return new(byCase ? PlaceUse.ByCase : PlaceUse.Exclusive, listed);
                }

                if (need.Use == PlaceUse.Consuming && ReferenceEquals(place, owned))
                {
                    return new(PlaceUse.Consuming, listed);
                }

                // SPEC 7.7.3: past a raw pointer's pointee the environment's write authority is what an access needs. A pointer derived
                // from an environment pointer (Below) is that pointer, and a pointer read through a raw Place the environment reaches is
                // read from the environment (the owner's O-76 (c) (i)), so the walk continues past a raw step.
                if (!ReferenceEquals(Below(place.Parent!, out var step), place))
                {
                    return new(PlaceUse.Shared, listed);
                }

                raw |= step == PathStep.RawFollow;
            }
        }

        // SPEC 5.2.1 condition 3 ("every pointer derived from it (copies, ...)"), 7.7.3 (R7-Y2c): what each pointer-typed body local's
        // definitions hold, the union of their roots' (HoldsOf), the least fixpoint; each pass reads every definition, and the passes are
        // bounded by the locals.
        private void Derive()
        {
            for (var changed = true; changed;)
            {
                changed = false;
                this.Work += this.definitions.Count;
                foreach (var (local, root) in this.definitions)
                {
                    var held = this.holds[local] | this.HoldsOf(root);
                    changed |= held != this.holds[local];
                    this.holds[local] = held;
                }
            }
        }

        // What a root holds: a capture of this binding, an environment pointer; a body local with definitions, what they hold so far; any
        // other root (a call's result, the owner's O-76 (b) (i), an allocation, a literal, a parameter), a pointer of its own.
        private Holds HoldsOf(Koto root) =>
            root.BoundSymbol is { Kind: BindingSymbolKind.Capture } capture && this.CaptureOf(capture) >= 0 ? Holds.Environment
            : root.BoundSymbol is { Kind: BindingSymbolKind.Local } local && this.holds.TryGetValue(local, out var held) ? held : Holds.Own;

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
        // import's contract is prose the compiler cannot read (SPEC 22.6), so its call is kept as Undecided, a located Unsupported_Kd.
        private bool ContractWritesThrough(Koto place)
        {
            if (!ReferenceTypes.IsPointer(place.BoundType) || place.Parent is not InvocationKoto { BoundCall: { } call } invocation ||
                ArgumentOf(call.ArgumentOperations, place) is not { } argument)
            {
                return false;
            }

            if (call.Target.Declaration is FunctionKoto declaration && declaration.FindAttribute(AttributeKind.LibraryImport) is not null)
            {
                if (!this.Undecided.Contains(invocation))
                {
                    this.Undecided.Add(invocation);
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

        // A node's evaluated Op (Binding.OpOf), read once per Classify: a call's Op reads its N argument operations, so every capture use
        // of its consumer reads it once.
        private HirOp OpOf(Koto node) => this.OpOf(node, ref this.FactsOf(node));

        private HirOp OpOf(Koto node, ref Facts facts)
        {
            if ((facts.Marks & Marks.Op) == 0)
            {
                facts.Op = binding.OpOf(node);
                facts.Marks |= Marks.Op;
            }

            return facts.Op;
        }

        // The row of a node met, created when absent; it is valid until the next row is created.
        private ref Facts FactsOf(Koto node) => ref CollectionsMarshal.GetValueRefOrAddDefault(this.met, SourceRef.Of(node), out _);

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

            ref var facts = ref this.FactsOf(consumer);
            var op = closure ? HirOp.Closure : this.OpOf(consumer, ref facts);
            if (op is HirOp.Unset or HirOp.Error or HirOp.Syntax)
            {
                return null; // The HIR publisher's walk takes no child of such a consumer.
            }

            var walked = exclusive ? Marks.ExclusiveWalk : Marks.SharedWalk;
            if ((facts.Marks & walked) == 0)
            {
                facts.Marks |= walked;
                var record = new TakeRecord(binding, this.met, exclusive);
                binding.AdjustChildren(consumer, op, exclusive, ref record);
                this.Work += record.Count;
            }

            return this.met.TryGetValue(SourceRef.Written(target), out var taken) ? taken.Takes(exclusive) : null;
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

        // What a pointer-typed body local's definitions hold (R7-Y2c): a pointer derived from an environment pointer, one of its own, or both
        // on some definitions, where no flow fact states which one reaches a use.
        [Flags]
        private enum Holds : byte
        {
            None = 0,

            // A pointer of its own: a call's result, an allocation, a literal, a parameter.
            Own = 1,

            // A pointer derived from an environment pointer.
            Environment = 2,
        }

        // What a Classify knows of a node (Facts.Marks): its Op is read (OpOf); its children's takes are recorded under the shared and the
        // exclusive selection (TakesOf); its consumer's record took it under each (TakeRecord).
        [Flags]
        private enum Marks : byte
        {
            None = 0,
            Op = 1,
            SharedWalk = 2,
            ExclusiveWalk = 4,
            SharedTake = 8,
            ExclusiveTake = 16,
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

        // What one Classify knows of a node it met: its evaluated Op (OpOf) and its takes by its consumer under each selection (TakeRecord),
        // each read only where its mark is set.
        private struct Facts
        {
            public ConsumedTakes Shared;
            public ConsumedTakes Exclusive;
            public HirOp Op;
            public Marks Marks;

            // The node's takes under a selection, or null where its consumer's record took it under none.
            public readonly ConsumedTakes? Takes(bool exclusive) =>
                (this.Marks & (exclusive ? Marks.ExclusiveTake : Marks.SharedTake)) == 0 ? null : exclusive ? this.Exclusive : this.Shared;
        }

        // The record of one consumer's takes under one selection (TakesOf), every child's as the publisher's RowUses writes them: each
        // completed by the claim of its two rows so far (Claimed, DECISIONS R7-M8).
        private struct TakeRecord(Binding binding, Dictionary<SourceRef, Facts> met, bool exclusive) : IHirUses
        {
            private int call = -1;

            // The takes recorded (ClosureWork).
            public int Count { get; private set; }

            public void Call(int ordinal) => this.call = ordinal;

            public void Use(Koto? child, HirAdaptation adaptation, UseKind kind)
            {
                if (child is not null)
                {
                    ref var takes = ref this.Row(child, out var taken);
                    this.Fix(ref takes, binding.ChildAdjust(child, adaptation, kind, taken && binding.Claimed(child, takes.Last, takes.Earlier)));
                }
            }

            public void Use(Koto? child, in BoundArgumentOperation operation)
            {
                if (child is not null)
                {
                    ref var takes = ref this.Row(child, out var taken);
                    this.Fix(ref takes, binding.ChildAdjust(child, operation, taken && binding.Claimed(child, takes.Last, takes.Earlier)));
                }
            }

            public void FixAdjust(Koto? child, HirAdjust adjust, HirPlace place)
            {
                if (child is not null)
                {
                    this.Fix(ref this.Row(child, out _), adjust);
                }
            }

            // The child's takes so far under this selection, marked taken for the take that follows (Fix), and whether one was recorded.
            private readonly ref ConsumedTakes Row(Koto child, out bool taken)
            {
                ref var facts = ref CollectionsMarshal.GetValueRefOrAddDefault(met, SourceRef.Of(child), out _);
                var mark = exclusive ? Marks.ExclusiveTake : Marks.SharedTake;
                taken = (facts.Marks & mark) != 0;
                facts.Marks |= mark;
                return ref exclusive ? ref facts.Exclusive : ref facts.Shared;
            }

            private void Fix(ref ConsumedTakes takes, HirAdjust adjust)
            {
                this.Count++;
                takes = new(adjust, this.call >= 0 ? EarlierAdjust(this.call, takes.Last, adjust) : takes.Earlier);
            }
        }
    }
}
