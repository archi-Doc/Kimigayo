// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Bare acquisition (SPEC 3.5, 3.5.1, 10.2, 14.2.2): a position without a fixed expected Type acquires its operand by bare acquisition.
// - Responsibility: the positions that do (IsBareAcquisitionPosition, a discarded node among them: Discards) and the Reborrow of a Place
//   storing an exclusive reference there (StoredReborrow), recorded as the node's one adaptation, exactly as an annotation of the same
//   complete Type would record it; the Reborrow by the Place's per-case plan, a pair layer's included, is ReborrowedType's, which a
//   nested capture entry and G721's interim row (1) read too (Binding.Captures).
// - Inputs: the node, its bound Type, its Place (IsBarePlace), its position and Discard Context (IsValueContext), the common
//   adaptation (ExpectedAdaptation), which names the Reborrow, and the reach of the Place (its containment walk under a case assignment).
// - Outputs: the recorded adaptation and its Type, the Reborrow's Type or the stored Type, and the limit (no one Type carries the
//   Reborrow); the bare entries of pair layers per Semantics case are Binding.PairCases'; how a consumer takes each child is the Adjust
//   rule's (Binding.Hir.Adjust.cs).
// - Invariants: a position with a fixed expected Type adapts by the common adaptation instead, and Reborrows a fitting Place that stores
//   an exclusive reference by the same predicate (StoresExclusiveReference). The plan is BareAcquisition's, evaluated again per owner
//   case, with no second plan; the reach is the one containment walk, run only where a layer is named; no warm allocation.
// - Prohibitions: no consumer decides by its own position test whether a bare acquisition Reborrows; no family row (OuterOrigin has
//   none); no branch per form.
public sealed partial class Binding
{
    // How far ReborrowedType's limit holds: the Type and plan allow no one Type for the Reborrow (Possible).
    private enum ReborrowLimit : byte
    {
        None,
        Possible,
    }

    // SPEC 3.5, 3.5.1, 10.2: the positions without a fixed expected Type that acquire any Place operand by bare acquisition, each
    // aggregate element separately: a Tuple's or array's element and a Dictionary literal's key and value (PLAN G256). Arguments,
    // assignment sources and results take their expected Type from the declaration and adapt there instead. A result source
    // (SPEC 14.9.1, 14.9.2) without a fixed Target Result Type is one: an anonymous function's expression body without a written or
    // expected result Type, a jump's operand, and a selection's or `do`'s body when its value is used (ResultTarget); the join's
    // common Type is chosen from the Types its sources take here. A discarded node (Discards) is acquired as at a value position.
    private static bool IsBareAcquisitionPosition(Koto node)
    {
        var target = Grouped(node);
        return target.Parent switch
        {
            VariableKoto variable => ReferenceEquals(variable.InitializerKoto, target),
            TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto => true,
            FunctionKoto { IsAnonymous: true } function => ReferenceEquals(function.ExpressionBody, target),
            JumpKoto jump => ReferenceEquals(jump.Expression, target),
            _ => Discards(target) || (ResultTarget(target) is IfKoto or MatchKoto or DoKoto && IsValueContext(target)),
        };
    }

    // SPEC 14.2.2, 14.2.4: whether a container or an explicit discard discards the value of `target`, the node it takes (Grouped): an
    // explicit discard's operand, or a node in Discard Context (a standalone expression, an indented body's direct expression, a
    // discarding single-item body). IsBareAcquisitionPosition and the HIR publisher's discarded Type (PlanRow.DiscardedType) read it.
    private static bool Discards(Koto target)
        => target.Parent is DiscardKoto discard ? ReferenceEquals(discard.Operand, target) : !IsValueContext(target);

    // The outermost group around `node`, the node its consumer takes (DECISIONS R4-M145): IsBareAcquisitionPosition, ClosureEffects'
    // TakenByCase and the HIR publisher's discard rows read this one rule.
    private static Koto Grouped(Koto node)
    {
        while (IsGroup(node.Parent))
        {
            node = node.Parent!;
        }

        return node;
    }

    // Whether a node passes its operand to its consumer as it is: parentheses, and a label, which wraps only a control-flow construct
    // (SPEC 14.4; the parser's label rule), never a Place. HirOp.Group is wider: an identity or transfer conversion, which acquires its
    // operand, is no such group.
    private static bool IsGroup(Koto? node) => node is ParenthesizedKoto or LabeledKoto;

    // SPEC 3.5.1: a stored `uniq/T` or `objuniq/T`, the bare Reborrow rule's first row (ReborrowedType), whose shape StoresExclusiveReference
    // reads: one definition.
    private static bool StoresConcreteExclusive(BoundType stored)
        => stored is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 };

    // A pair layer applied to a target (`s/X`, a Semantics application of one component), whose owner case's plan is the target's own
    // (ReborrowedType's recursion); the original `s/T` and a family have none, so their Reborrow never limits.
    private static bool AppliesToTarget(BoundType stored) => stored is { Kind: BoundTypeKind.SemanticsApplication, Components: [_] };

    // SPEC 3.5.1, 10.2.2, 8.14: whether a node is a bare Place whose bare acquisition may Reborrow in its own Semantics, at a bare
    // acquisition position (StoredReborrow) and at a fixed expected Type it fits alike: one storing `uniq/T` or `objuniq/T`, or a pair layer
    // of any form (TryAdaptationSelector), whose per-case plan the adaptation reads once (ReborrowedType). The Type is tested before the
    // Place: every bound node asks, and few store either.
    private bool StoresExclusiveReference(Koto node, BoundType actual)
        => (StoresConcreteExclusive(actual) || TryAdaptationSelector(actual, out _)) && node.ErasedFunctionType is null && this.IsBarePlace(node);

    // SPEC 15.6.3, 15.8.2, 8.15.2: the meet of the safe borrow layers that reach a Place under a case assignment (the cases its walk's pair
    // steps read; empty for every case), `static` when none (the one containment walk, ρ for a root reached in a Closure body included).
    private BoundOrigin ReachOf(Koto place, ReadOnlySpan<PairCase> cases)
        => this.ContainedOrigin(place, new() { Met = BoundOrigin.Static, Dereferenced = true, Cases = cases }) ?? BoundOrigin.Static;

    // SPEC 3.5.1, 8.14, 8.15.2, 7.7.2, 8.1.2, 15.6.3 (DECISIONS BU-27 and its G254b amendment): the Type of the same-Semantics Reborrow that
    // a bare acquisition of a Place storing `stored` makes by its per-case plan under `cases` (empty at the Place; inside, the owner cases of
    // the pair layers above), null where it names none (no Reborrow, an incomplete plan, a family). Each layer it Reborrows has its Origin
    // met with the reach (Shortened): `reach`, a nested capture entry's enclosing receiver (EnvironmentContainment) in every case, else the
    // layers that reach the Place `node` under `cases` (ReachOf; the merged walk would meet slots only a Copy case has):
    // - a stored `uniq/T` or `objuniq/T` whose plan Reborrows (the caller's BareAcquire): its Origin;
    // - a pair layer whose plan (BareAcquisition) Reborrows the layer itself in an exclusive admitted case: its outer-Origin slot; and where
    //   its owner case's target Reborrows, that target replaced by the target's own Reborrow Type under `cases` extended by the owner case
    //   (BareAcquisition's recursion, in nested scratch); the stored Type where nothing is shortened;
    // - the limit: an owner case Reborrowing its target under a binder that also admits an invariant Semantics (InvariantAdmitted, SPEC
    //   15.3.5), whose case keeps that target exactly, so no one Type carries the Reborrow once a reach shortens the target; `limit` is
    //   Possible whatever the reach (a fact of the Type and plan, which G721's row (1) reads: Binding.Captures), and a shortened target is
    //   not replaced: the Type is the layer's own Reborrow alone, null where no exclusive case Reborrows it (a known class-1 defect, PLAN
    //   G254c, whose removal is a per-case Reborrow Type or the limit recorded at the selected input).
    // A family names no layer (OuterOrigin has no family row; PLAN G254's family limit).
    private BoundType? ReborrowedType(BoundType stored, Koto node, BoundOrigin? reach, ReadOnlySpan<PairCase> cases, out ReborrowLimit limit)
    {
        limit = ReborrowLimit.None;
        if (StoresConcreteExclusive(stored))
        {
            return this.Shortened(stored, stored.Origin!, reach ?? this.ReachOf(node, cases));
        }

        if (!TryAdaptationSelector(stored, out var whole))
        {
            return null;
        }

        var assigned = CaseOf(cases, whole.Symbol!);
        var mask = assigned?.ToMask() ?? SemanticsMask.All;
        if ((this.BareAcquisition(stored, node, cases, out var reborrow) & mask) != SemanticsMask.None || (reborrow & mask) == SemanticsMask.None)
        {
            return null;
        }

        var result = (reborrow & mask & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0 && this.OuterOrigin(stored) is { } slot
            ? this.Shortened(stored, slot, reach ?? this.ReachOf(node, cases)) : null;
        if ((reborrow & mask & SemanticsMask.Owner) == 0 || !AppliesToTarget(stored))
        {
            return result;
        }

        var target = stored.Components[0];

        var scratch = this.caseScratch.Rent(cases.Length + 1);
        BoundType? inner;
        try
        {
            var extended = scratch.AsSpan(0, cases.Length + 1);
            cases.CopyTo(extended);
            extended[^1] = new(whole.Symbol!, SemanticsKind.Owner);
            inner = this.ReborrowedType(target, node, reach, extended, out limit);
        }
        finally
        {
            this.caseScratch.Return(scratch, clearArray: true);
        }

        var outer = result ?? stored;
        var invariant = inner is not null && assigned is null && this.InvariantAdmitted(stored, node, default);
        limit = invariant ? ReborrowLimit.Possible : limit;
        if (inner is null || ReferenceEquals(inner, target))
        {
            return inner is null ? result : outer;
        }

        return invariant ? result : this.types.Rebuild(outer, [inner], outer.Origin, (BoundOrigin[])outer.OriginArguments);
    }

    // A Reborrowed layer's Origin met with `reach`, as each case's Reborrow of its concrete reference is. A pair slot exists only in the
    // borrow cases (PairSlotCondition), and a case that Copies keeps the met slot, which no verdict of it needs longer (SPEC 8.15.2). The
    // stored Type itself when `reach` shortens nothing (the original `s/T` states its `o` implicitly, OuterOrigin).
    private BoundType Shortened(BoundType stored, BoundOrigin layer, BoundOrigin reach)
        => this.Meet(layer, reach) is var met && ReferenceEquals(met, layer) ? stored : this.WithOrigins(stored, met, (BoundOrigin[])stored.OriginArguments);

    // SPEC 3.5.1: the bare acquisition of a Place storing an exclusive reference Reborrows it in its own Semantics, exactly as an
    // annotation of the same complete Type would; the Reborrow's Type is Binding's name for it (DECISIONS BU-27), which a join's common
    // Type takes from each such source. Returns that Type, null when the node is acquired otherwise.
    private BoundType? StoredReborrow(Koto node, BoundType? actual)
    {
        if (actual is not null && this.StoresExclusiveReference(node, actual) && IsBareAcquisitionPosition(node) &&
            this.ExpectedAdaptation(node, actual, actual) is { } reborrow)
        {
            this.adaptations[node] = reborrow;
            return reborrow.Type;
        }

        return null;
    }
}
