// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Origin fits (SPEC 15.3.5, 15.6.1, 15.6.5): the Origin positions at which a fit whose storage matches fails, and its record.
// - Responsibility: the first, or with `all` every, Origin position of a fit, in the order of the outer borrow layer, the Semantics
//   target, then slots and Type arguments, by the variance FitsTypeCore and CheckTypeUse compose, whose relation is not proven or,
//   with `judged`, that the relation judge fails (FailedOriginRelation, FailedOriginPart); for a meet at the longer end, the chain of
//   a failing operand, Refuted before Unknown (FailedChain, AddFailingChains, FailingOperand, OriginPartFails); RecordMismatch's
//   Origin part, a fit that fails only at Origin positions (RecordOriginFitFailure); and the reset of the pass's Origin relation
//   records, a failed node's first chain and its others (ResetOriginRecords).
// - Inputs: the fit's actual and expected Types and its use; the proof (ProvesOriginOutlives, VerifiedRegionOutlives), the judge
//   (OriginRelationFails), a local region's sources (LocalRegionSources) and the outer-Origin decision (FitOuterOrigins).
// - Outputs: OriginRelationFact values; RecordOriginFitFailure fails the node with its first chain in `originRelations` and the
//   other chains through RecordMoreRelation, completes it as a dependent of a failed Origin environment (FailedOriginEnvironment)
//   or fails it Unsupported, and returns whether a position failed; a record at a body chain adds the fit's position rows
//   (CheckTypeUse, DECISIONS R4-M108).
// - Invariants: the position search reads proof and judgment only and changes no inference, obligation or declaration state; an
//   `outlives` record names a failing operand's chain, the whole longer end only when no operand fails alone; `relationScratch`
//   is empty when RecordOriginFitFailure returns.
// - Prohibitions: no structural Type comparison (the caller decided that the storage matches); no message text (the diagnostics
//   adapter writes it from the facts).
public sealed partial class Binding
{
    // The failed chains of one fit (FailedOriginRelation with `all`), which RecordOriginFitFailure publishes.
    private readonly List<OriginRelationFact> relationScratch = new();

    // Resets the pass's Origin relation records at its start (ResetPrerequisites): each failed node's first chain and its others.
    private void ResetOriginRecords()
    {
        this.originRelations?.Clear();
        this.callRelations?.Clear();
    }

    // RecordMismatch's Origin part (SPEC 15.6.1, 15.6.5): whether a fit whose storage matches fails at an Origin position, which it
    // records. A position the relation judge fails records every failed chain; a position that is only unproven is recorded when an
    // end is unresolved and is otherwise Unsupported. Either is a dependent of a failed Origin environment instead, the first only
    // when no failed chain is Refuted. A record at a body chain (2a0-D D1, ends resolved at the use) gives every position of the fit,
    // fixed-fixed ones included, a row of its body for A5 (CheckTypeUse's obligations, up to its first differing Function Type
    // component); the records stay Binding's until H11 (DECISIONS R4-M108).
    private bool RecordOriginFitFailure(Koto node, Koto at, BoundType actual, BoundType expected)
    {
        var all = this.relationScratch;
        all.Clear();
        OriginRelationFact recorded;
        if (this.FailedOriginRelation(actual, expected, node, OriginVariance.Covariant, judged: true, all: all) is { } relation)
        {
            if (!relation.Refuted && this.FailedOriginEnvironment(node) is { } cause)
            {
                var refuted = false;
                for (var i = 0; i < all.Count; i++)
                {
                    refuted |= all[i].Refuted;
                }

                if (!refuted)
                {
                    all.Clear();
                    this.CompleteDependent(node, cause);
                    return true;
                }
            }

            recorded = relation;
        }
        else if (this.FailedOriginRelation(actual, expected, node, OriginVariance.Covariant) is { } unproven)
        {
            if (this.FailedOriginEnvironment(node) is { } cause)
            {
                this.CompleteDependent(node, cause);
                return true;
            }

            // No concrete lifetime was refuted above. An unresolved end without an identified failed contract remains
            // an independent proof failure; it must not be hidden by the presence of an unrelated declaration error.
            if (unproven.Longer.Kind != OriginKind.Unbound && unproven.Shorter.Kind != OriginKind.Unbound)
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

        // SPEC 15.6.1: every failed chain is reported; the records other than the node's own are published with it.
        var failed = node.BindingFailure == BindingFailure.None;
        var rows = false;
        for (var i = 0; i < all.Count && failed; i++)
        {
            rows |= BodyChain(this.OriginAtUse(all[i].Longer, node), this.OriginAtUse(all[i].Shorter, node));
            if (!all[i].Equals(recorded))
            {
                this.RecordMoreRelation(node, all[i] with { At = at, Destination = expected });
            }
        }

        all.Clear();
        this.FailExplained(ref this.originRelations, node, BindingFailure.OriginRelation, recorded with { At = at, Destination = expected });
        if (rows)
        {
            _ = this.CheckTypeUse(actual, expected, node);
        }

        return true;
    }

    // SPEC 15.6.1: the first Origin position of a fit, in the order of the outer borrow layer, the Semantics target, then slots and
    // Type arguments, whose relation is not proven; `==` at an invariant position. It is Refuted when the longer end is a body-local
    // finite Origin and the shorter end a fixed one, and Unknown otherwise. With `all`, every failing position, and every failing
    // operand of a meet at its longer end, is added in that order (SPEC 15.6.1: every failed chain is reported).
    // With `judged`, only a position that the relation judge fails counts (SPEC 15.6.5), as a body fit judges it; without it, any
    // unproven position does, as for a fit that judges Origins by proof alone.
    // `polarity` is the variance of the compared position within the fitted Type, composed as FitsTypeCore and CheckTypeUse compose
    // it (SPEC 15.3.5): a contravariant position names the reverse relation, and an invariant or unused one an `==`, also for every
    // position nested in it, such as the slots of a Type argument stored through `uniq/T` or read by `(T) -> i32`.
    private OriginRelationFact? FailedOriginRelation(BoundType actual, BoundType expected, Koto use, OriginVariance polarity, int depth = 0, bool judged = false, List<OriginRelationFact>? all = null, bool verified = false)
    {
        if (depth > 64)
        {
            return null;
        }

        OriginRelationFact? first = null;
        // SPEC 8.1.2, 15.6.5: the outer Origins as the fit relates them; a pair layer's slot exists only in its binder's borrow cases,
        // whose premises the relation may use.
        if (FitOuterOrigins(actual, expected, this, use, out var outer, out var required) == OuterOriginFit.Relation &&
            this.FailedOriginPart(outer, required, expected, use, polarity, judged, all, verified, SlotCondition(actual, this, use)) is { } layer)
        {
            if (all is null)
            {
                return layer;
            }

            first ??= layer;
        }

        var exclusive = IsInvariantLayer(actual, this);
        for (var i = 0; i < actual.Components.Count && i < expected.Components.Count && actual.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication; i++)
        {
            var target = exclusive ? OriginVariance.Invariant : polarity;
            if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], use, target, depth + 1, judged, all, verified) is { } failed)
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
            if (this.FailedOriginPart(actual.OriginArguments[i], expected.OriginArguments[i], expected, use, variance, judged, all, verified) is { } slot)
            {
                if (all is null)
                {
                    return slot;
                }

                first ??= slot;
            }
        }

        for (var i = 0; i < actual.Components.Count && i < expected.Components.Count && actual.Kind is not (BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication); i++)
        {
            var position = actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count ? schema.GenericSlots[i].OriginVariance
                : actual.Kind == BoundTypeKind.Function && actual.Components.Count == 2 && i == 0 ? OriginVariance.Contravariant
                : OriginVariance.Covariant;
            if (this.FailedOriginRelation(actual.Components[i], expected.Components[i], use, ComposeVariance(polarity, position), depth + 1, judged, all, verified) is { } argument)
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
    private OriginRelationFact? FailedOriginPart(BoundOrigin actual, BoundOrigin expected, BoundType type, Koto use, OriginVariance variance, bool judged, List<OriginRelationFact>? all = null, bool verified = false, ulong condition = 0)
    {
        if (ReferenceEquals(actual, expected))
        {
            return null;
        }

        if (variance == OriginVariance.Contravariant)
        {
            return Fails(expected, actual) ? this.FailedChain(use, expected, actual, type, judged, all, condition) : null;
        }

        if (variance == OriginVariance.Covariant)
        {
            return Fails(actual, expected) ? this.FailedChain(use, actual, expected, type, judged, all, condition) : null;
        }

        if (!Fails(actual, expected) && !Fails(expected, actual))
        {
            return null;
        }

        OriginRelationFact equality = new(use, actual, expected, true, type, RefutesOriginRelation(actual, expected));
        all?.Add(equality);
        return equality;

        bool Fails(BoundOrigin longer, BoundOrigin shorter) => verified
            ? !this.VerifiedRegionOutlives(longer, shorter, use)
            : this.OriginPartFails(longer, shorter, use, judged, condition);
    }

    // SPEC 15.3.6, 15.6.1: a meet at the longer end outlives an Origin exactly when each operand does, so an `outlives` relation names
    // the chain of one failing operand, never the meet itself, and keeps the whole meet for a result bound. With `all`, the chain of
    // every failing operand is added in the meet's order (every failed chain is reported).
    private OriginRelationFact FailedChain(Koto use, BoundOrigin longer, BoundOrigin shorter, BoundType type, bool judged, List<OriginRelationFact>? all = null, ulong condition = 0)
    {
        var operand = this.FailingOperand(longer, shorter, use, judged, true, condition) ?? this.FailingOperand(longer, shorter, use, judged, false, condition) ?? longer;
        OriginRelationFact chain = new(use, operand, shorter, false, type, RefutesOriginRelation(operand, shorter));
        if (all is not null && (ReferenceEquals(operand, longer) || !this.AddFailingChains(use, longer, shorter, type, judged, all, condition)))
        {
            all.Add(chain);
        }

        return chain;
    }

    // Adds the chain of each failing operand of `origin`, nested meets flattened in order; whether any was added.
    private bool AddFailingChains(Koto use, BoundOrigin origin, BoundOrigin shorter, BoundType type, bool judged, List<OriginRelationFact> all, ulong condition = 0)
    {
        if (IsLocalRegion(origin))
        {
            var found = false;
            foreach (var source in this.LocalRegionSources(origin))
            {
                found |= this.AddFailingChains(use, source, shorter, type, judged, all, condition);
            }

            return found;
        }

        if (origin.Kind != OriginKind.Intersection)
        {
            if (!this.OriginPartFails(origin, shorter, use, judged, condition))
            {
                return false;
            }

            all.Add(new(use, origin, shorter, false, type, RefutesOriginRelation(origin, shorter)));
            return true;
        }

        var added = false;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            added |= this.AddFailingChains(use, origin.Operands[i], shorter, type, judged, all, condition);
        }

        return added;
    }

    private bool OriginPartFails(BoundOrigin longer, BoundOrigin shorter, Koto use, bool judged, ulong condition = 0)
        => !this.ProvesOriginOutlives(longer, shorter, use, condition) && (!judged || this.OriginRelationFails(longer, shorter, use, condition));

    // SPEC 15.6.5: the failing operand that the record names follows the judgment, a Refuted chain, which decides the record's code,
    // before an Unknown one, each first in the meet's order; with `refuted`, only a Refuted chain counts.
    private BoundOrigin? FailingOperand(BoundOrigin longer, BoundOrigin shorter, Koto use, bool judged, bool refuted, ulong condition = 0)
    {
        if (IsLocalRegion(longer))
        {
            foreach (var source in this.LocalRegionSources(longer))
            {
                if (this.FailingOperand(source, shorter, use, judged, refuted, condition) is { } failed)
                {
                    return failed;
                }
            }

            return null;
        }

        if (longer.Kind != OriginKind.Intersection)
        {
            return (!refuted || RefutesOriginRelation(longer, shorter)) && this.OriginPartFails(longer, shorter, use, judged, condition) ? longer : null;
        }

        for (var i = 0; i < longer.Operands.Count; i++)
        {
            if (this.FailingOperand(longer.Operands[i], shorter, use, judged, refuted, condition) is { } operand)
            {
                return operand;
            }
        }

        return null;
    }
}
