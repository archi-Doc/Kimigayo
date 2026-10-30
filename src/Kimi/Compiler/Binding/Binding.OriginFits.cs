// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Origin fits (SPEC 15.3.5, 15.6.1, 15.6.5): the Origin positions at which a fit whose storage matches fails, and its record.
// - Responsibility: the position search of a fit or a relation, one non-allocating value per search (OriginFitSearch, its own contract
//   below: the failing positions by composed variance and the failing operand chains of a meet, in its body and mode, OriginFitMode);
//   RecordMismatch's Origin part, a fit that fails only at Origin positions (RecordOriginFitFailure); and the reset of the pass's
//   Origin relation records, a failed node's first chain and its others (ResetOriginRecords).
// - Inputs: the fit's actual and expected Types and its use; the proof (ProvesOriginOutlives, VerifiedRegionOutlives), the judge
//   (OriginRelationFails), a local region's sources (LocalRegionSources) and the outer-Origin decision (FitOuterOrigins).
// - Outputs: OriginRelationFact values; RecordOriginFitFailure fails the node with its first chain in `originRelations` and the
//   other chains through RecordMoreRelation, completes it as a dependent of a failed Origin environment (FailedOriginEnvironment)
//   or fails it Unsupported, and returns whether a position failed; a record at a body chain adds the fit's position rows
//   (CheckTypeUse, DECISIONS R4-M108) and makes its node a twin (IsTwinRecord, DECISIONS R4-M111).
// - Invariants: the position search reads proof and judgment only and changes no inference, obligation or declaration state;
//   `relationScratch` is empty when RecordOriginFitFailure returns.
// - Prohibitions: no structural Type comparison (the caller decided that the storage matches); no message text (the diagnostics
//   adapter writes it from the facts).
public sealed partial class Binding
{
    // The failed chains of one fit (FailedOriginRelation with `all`), which RecordOriginFitFailure publishes.
    private readonly List<OriginRelationFact> relationScratch = new();

    // The pass's twin nodes (DECISIONS R4-M108, R4-M111): a fit's node with a recorded body chain and a local clause whose record is one,
    // each added where its rows are formed.
    private HashSet<Koto>? originTwins;

    // SPEC 15.6.1, 15.6.5: how OriginFitSearch decides that a position's relation fails: unproven by the premises visible at the use
    // (Proof), also failed by the relation judge in the search's body (Judged, a body fit), or not proven by the verified regions
    // (Verified, lowering's recheck of a verified contract).
    private enum OriginFitMode : byte
    {
        Proof,
        Judged,
        Verified,
    }

    /// <summary>Gets whether a node's Origin relation records are twins, the records of a relation that is also a row A5 judges, which the
    /// MIR difference ledger compares with A5 (DECISIONS R4-M108, R4-M111).</summary>
    /// <param name="node">The records' node.</param>
    /// <returns>Whether its records are twins.</returns>
    [Transitional("R6", Reason = "The ledger's twin subset; H11 deletes it with the twins (DECISIONS R4-M111).")]
    internal bool IsTwinRecord(Koto node) => this.originTwins?.Contains(node) == true;

    // DIAGNOSTICS (Origin relations): an obligation's record is at its use, a local annotation's fit at the local's initializer, the
    // value that supplies the longer end, as a binding-time fit (RecordMismatch's `at`) and the old reporter record it.
    private static Koto RelationAt(in BindingObligation obligation)
        => IsFitObligation(obligation) && obligation.Use is VariableKoto { InitializerKoto: { } initializer } ? initializer : obligation.Use;

    // Resets the pass's Origin relation records at its start (ResetPrerequisites): each failed node's first chain and its others, and the
    // twin nodes.
    private void ResetOriginRecords()
    {
        this.originRelations?.Clear();
        this.callRelations?.Clear();
        this.originTwins?.Clear();
    }

    // RecordMismatch's Origin part (SPEC 15.6.1, 15.6.5): whether a fit whose storage matches fails at an Origin position, which it
    // records (RecordOriginChains). A position the relation judge fails records every failed chain; a position that is only unproven is
    // recorded when an end is unresolved and is otherwise Unsupported, unless a failed Origin environment explains it. A record at a
    // body chain (2a0-D D1, ends resolved at the use) gives every position of the fit, fixed-fixed ones included, a row of its body for
    // A5 (CheckTypeUse's obligations, up to its first differing Function Type component); the records stay Binding's until H11
    // (DECISIONS R4-M108).
    private bool RecordOriginFitFailure(Koto node, Koto at, BoundType actual, BoundType expected)
    {
        var all = this.relationScratch;
        all.Clear();
        OriginRelationFact recorded;
        if (new OriginFitSearch(this, node, this.formingBody, OriginFitMode.Judged, all).FailedOriginRelation(actual, expected, OriginVariance.Covariant) is
            { } relation)
        {
            recorded = relation;
        }
        else if (new OriginFitSearch(this, node, this.formingBody, OriginFitMode.Proof).FailedOriginRelation(actual, expected, OriginVariance.Covariant) is
            { } unproven)
        {
            // No concrete lifetime was refuted above. An unresolved end without an identified failed contract remains
            // an independent proof failure; it must not be hidden by the presence of an unrelated declaration error.
            if (unproven.Longer.Kind != OriginKind.Unbound && unproven.Shorter.Kind != OriginKind.Unbound && this.FailedOriginEnvironment(node, unproven) is null)
            {
                this.Fail(node, BindingFailure.Unsupported);
                return true;
            }

            all.Add(recorded = unproven);
        }
        else
        {
            return false;
        }

        if (this.RecordOriginChains(node, at, this.formingBody, expected, recorded))
        {
            _ = this.CheckTypeUse(actual, expected, node);
        }

        return true;
    }

    // SPEC 15.6.1, 23.1.2 (DECISIONS R4-M133): the one recording of an Origin relation failure's chains, a fit's at binding time and a
    // pending relation's at its Closure's classification: the chains in `relationScratch`, `recorded` the node's own record and the
    // others published with it (RecordMoreRelation), each at `at` with the destination Type (none for a relation without one). A
    // failure that no chain refutes is a dependent of the failed clause its failed Origin environment names (FailedOriginEnvironment;
    // DECISIONS BU-69) instead. Returns whether a recorded
    // chain is a body chain of `body`, its ends resolved at the use; the node is then a twin (DECISIONS R4-M108, R4-M111).
    private bool RecordOriginChains(Koto node, Koto at, ExecutableBody body, BoundType? destination, OriginRelationFact recorded)
    {
        var all = this.relationScratch;
        var refuted = recorded.Refuted;
        for (var i = 0; i < all.Count; i++)
        {
            refuted |= all[i].Refuted;
        }

        if (!refuted && this.FailedOriginEnvironment(node, recorded) is { } cause)
        {
            all.Clear();
            this.CompleteDependent(node, cause);
            return false;
        }

        // SPEC 15.6.1: every failed chain is reported; the records other than the node's own are published with it.
        var rows = false;
        for (var i = 0; i < all.Count && node.BindingFailure == BindingFailure.None; i++)
        {
            rows |= BodyChain(this.OriginAtUse(all[i].Longer, node), this.OriginAtUse(all[i].Shorter, node), body);
            if (!all[i].Equals(recorded))
            {
                this.RecordMoreRelation(node, all[i] with { At = at, Destination = destination });
            }
        }

        all.Clear();
        this.FailExplained(ref this.originRelations, node, BindingFailure.OriginRelation, recorded with { At = at, Destination = destination });
        if (rows)
        {
            (this.originTwins ??= new(ReferenceEqualityComparer.Instance)).Add(node);
        }

        return rows;
    }

    // The position search of one fit or one relation (SPEC 15.6.1, 15.6.5; DECISIONS R4-M133).
    // - Responsibility: the first, or with `all` every, Origin position whose relation fails, in the order of the outer borrow layer, the
    //   Semantics target, then slots and Type arguments, by the variance FitsTypeCore and CheckTypeUse compose (FailedOriginRelation,
    //   FailedOriginPart); for a meet at the longer end, the chain of a failing operand, Refuted before Unknown (FailedChain,
    //   AddFailingChains, FailingOperand, OriginPartFails).
    // - Inputs, its constant context: the use, the body that formed the relation (whose classes and judge decide its chains: an
    //   obligation's Body, or the forming body while binding), the mode (OriginFitMode) and the chain list; per call, the Types or
    //   Origins compared, their variance and the Semantics case.
    // - Outputs: OriginRelationFact values at the use, without a destination (RecordOriginChains names it), every one added to `all`
    //   when it is given.
    // - Invariants: reads proof and judgment only and changes no inference, obligation or declaration state; an `outlives` record names a
    //   failing operand's chain, the whole longer end only when no operand fails alone; a value of the struct allocates nothing.
    // - Prohibitions: no record or failure (the callers record); no body read from the forming context.
    private readonly struct OriginFitSearch(Binding binding, Koto use, ExecutableBody body, OriginFitMode mode, List<OriginRelationFact>? all = null)
    {
        // SPEC 15.6.1: the first Origin position of a fit, in the order of the outer borrow layer, the Semantics target, then slots and
        // Type arguments, whose relation fails; `==` at an invariant position. It is Refuted when the longer end is a body-local finite
        // Origin and the shorter end a fixed one, and Unknown otherwise. With `all`, every failing position, and every failing operand of
        // a meet at its longer end, is added in that order (SPEC 15.6.1: every failed chain is reported).
        // `polarity` is the variance of the compared position within the fitted Type, composed as FitsTypeCore and CheckTypeUse compose
        // it (SPEC 15.3.5): a contravariant position names the reverse relation, and an invariant or unused one an `==`, also for every
        // position nested in it, such as the slots of a Type argument stored through `uniq/T` or read by `(T) -> i32`.
        internal OriginRelationFact? FailedOriginRelation(BoundType actual, BoundType expected, OriginVariance polarity, int depth = 0)
        {
            if (depth > 64)
            {
                return null;
            }

            OriginRelationFact? first = null;
            // SPEC 8.1.2, 15.6.5: the outer Origins as the fit relates them; a pair layer's slot exists only in its binder's borrow cases,
            // whose premises the relation may use.
            if (FitOuterOrigins(actual, expected, binding, use, out var outer, out var required) == OuterOriginFit.Relation &&
                this.FailedOriginPart(outer, required, polarity, SlotCondition(actual, binding, use)) is { } layer)
            {
                if (all is null)
                {
                    return layer;
                }

                first ??= layer;
            }

            var exclusive = IsInvariantLayer(actual, binding);
            for (var i = 0; i < actual.Components.Count && i < expected.Components.Count &&
                actual.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication; i++)
            {
                var target = exclusive ? OriginVariance.Invariant : polarity;
                if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], target, depth + 1) is { } failed)
                {
                    if (all is null)
                    {
                        return failed;
                    }

                    first ??= failed;
                }
            }

            for (var i = 0; i < actual.OriginArguments.Count && i < expected.OriginArguments.Count; i++)
            {
                var variance = ComposeVariance(polarity, expected.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant);
                if (this.FailedOriginPart(actual.OriginArguments[i], expected.OriginArguments[i], variance) is { } slot)
                {
                    if (all is null)
                    {
                        return slot;
                    }

                    first ??= slot;
                }
            }

            for (var i = 0; i < actual.Components.Count && i < expected.Components.Count &&
                actual.Kind is not (BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication); i++)
            {
                var position = actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count
                    ? schema.GenericSlots[i].OriginVariance
                    : actual.Kind == BoundTypeKind.Function && actual.Components.Count == 2 && i == 0 ? OriginVariance.Contravariant
                    : OriginVariance.Covariant;
                if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], ComposeVariance(polarity, position), depth + 1) is { } argument)
                {
                    if (all is null)
                    {
                        return argument;
                    }

                    first ??= argument;
                }
            }

            return first;
        }

        // One Origin position of a fit by its variance: `actual` outlives `expected` at a covariant position, the reverse at a
        // contravariant one, and both, as `==` with `actual` first, at an invariant one.
        internal OriginRelationFact? FailedOriginPart(BoundOrigin actual, BoundOrigin expected, OriginVariance variance, ulong condition = 0)
        {
            if (ReferenceEquals(actual, expected))
            {
                return null;
            }

            if (variance == OriginVariance.Contravariant)
            {
                return this.Fails(expected, actual, condition) ? this.FailedChain(expected, actual, condition) : null;
            }

            if (variance == OriginVariance.Covariant)
            {
                return this.Fails(actual, expected, condition) ? this.FailedChain(actual, expected, condition) : null;
            }

            if (!this.Fails(actual, expected, condition) && !this.Fails(expected, actual, condition))
            {
                return null;
            }

            var refuted = RefutesOriginRelation(actual, expected, body);
            OriginRelationFact equality = new(use, actual, binding.SuppliedAtUse(actual, use), expected, true, null, refuted, Condition: condition);
            all?.Add(equality);
            return equality;
        }

        // Whether a position's relation fails in the search's mode: one the verified regions do not prove, or one OriginPartFails fails.
        private bool Fails(BoundOrigin longer, BoundOrigin shorter, ulong condition)
            => mode == OriginFitMode.Verified ? !binding.VerifiedRegionOutlives(longer, shorter, use) : this.OriginPartFails(longer, shorter, condition);

        // SPEC 15.3.6, 15.6.1: a meet at the longer end outlives an Origin exactly when each operand does, so an `outlives` relation names
        // the chain of one failing operand, never the meet itself, and keeps the whole meet for a result bound. With `all`, the chain of
        // every failing operand is added in the meet's order (every failed chain is reported).
        private OriginRelationFact FailedChain(BoundOrigin longer, BoundOrigin shorter, ulong condition)
        {
            var operand = this.FailingOperand(longer, shorter, true, condition) ?? this.FailingOperand(longer, shorter, false, condition) ?? longer;
            var refuted = RefutesOriginRelation(operand, shorter, body);
            OriginRelationFact chain = new(use, operand, binding.SuppliedAtUse(operand, use), shorter, false, null, refuted, Condition: condition);
            if (all is not null && (ReferenceEquals(operand, longer) || !this.AddFailingChains(longer, shorter, condition)))
            {
                all.Add(chain);
            }

            return chain;
        }

        // Adds the chain of each failing operand of `origin`, nested meets flattened in order; whether any was added.
        private bool AddFailingChains(BoundOrigin origin, BoundOrigin shorter, ulong condition)
        {
            if (RegionIn(origin, body))
            {
                var found = false;
                foreach (var source in binding.LocalRegionSources(origin))
                {
                    found |= this.AddFailingChains(source, shorter, condition);
                }

                return found;
            }

            if (origin.Kind != OriginKind.Intersection)
            {
                if (!this.OriginPartFails(origin, shorter, condition))
                {
                    return false;
                }

                all!.Add(new(use, origin, binding.SuppliedAtUse(origin, use), shorter, false, null, RefutesOriginRelation(origin, shorter, body), Condition: condition));
                return true;
            }

            var added = false;
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                added |= this.AddFailingChains(origin.Operands[i], shorter, condition);
            }

            return added;
        }

        // Whether a relation is not proven and, judged, the relation judge fails it in the search's body (SPEC 15.6.5).
        private bool OriginPartFails(BoundOrigin longer, BoundOrigin shorter, ulong condition)
            => !binding.ProvesOriginOutlives(longer, shorter, use, condition) &&
                (mode != OriginFitMode.Judged || binding.OriginRelationFails(longer, shorter, use, body, condition));

        // SPEC 15.6.5: the failing operand that the record names follows the judgment, a Refuted chain, which decides the record's code,
        // before an Unknown one, each first in the meet's order; with `refuted`, only a Refuted chain counts.
        private BoundOrigin? FailingOperand(BoundOrigin longer, BoundOrigin shorter, bool refuted, ulong condition)
        {
            if (RegionIn(longer, body))
            {
                foreach (var source in binding.LocalRegionSources(longer))
                {
                    if (this.FailingOperand(source, shorter, refuted, condition) is { } failed)
                    {
                        return failed;
                    }
                }

                return null;
            }

            if (longer.Kind != OriginKind.Intersection)
            {
                return (!refuted || RefutesOriginRelation(longer, shorter, body)) && this.OriginPartFails(longer, shorter, condition) ? longer : null;
            }

            for (var i = 0; i < longer.Operands.Count; i++)
            {
                if (this.FailingOperand(longer.Operands[i], shorter, refuted, condition) is { } operand)
                {
                    return operand;
                }
            }

            return null;
        }
    }
}
