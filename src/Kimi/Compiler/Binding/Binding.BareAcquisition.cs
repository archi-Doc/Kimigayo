// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Bare acquisition (SPEC 3.5, 3.5.1, 10.2, 14.2.2): a position without a fixed expected Type acquires its operand by bare acquisition.
// - Responsibility: the positions that do (IsBareAcquisitionPosition, a discarded node among them: Discards) and the Reborrow of a Place
//   storing an exclusive reference there (StoredReborrow), recorded as the node's one adaptation, exactly as an annotation of the same
//   complete Type would record it; the Reborrow by the Place's per-case plan, a pair layer's included, is BareReborrowLayer's and
//   ReborrowedType's, which a nested capture entry reads too (Binding.Captures).
// - Inputs: the node, its bound Type, its Place (IsBarePlace), its position and Discard Context (IsValueContext), and the
//   common adaptation (ExpectedAdaptation), which names the Reborrow.
// - Outputs: the recorded adaptation and its Type; the bare entries of pair layers per Semantics case are Binding.PairCases'; how a
//   consumer takes each child is the Adjust rule's (Binding.Hir.Adjust.cs).
// - Invariants: a position with a fixed expected Type adapts by the common adaptation instead, and Reborrows a fitting Place that stores
//   an exclusive reference by the same predicate (StoresExclusiveReference).
// - Prohibitions: no consumer decides by its own position test whether a bare acquisition Reborrows.
public sealed partial class Binding
{
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

    // SPEC 3.5.1, 10.2.2, 8.14: whether a node is a bare Place whose bare acquisition may Reborrow in its own Semantics, at a bare
    // acquisition position (StoredReborrow) and at a fixed expected Type it fits alike: one storing `uniq/T` or `objuniq/T`, or a pair layer
    // of any form (TryAdaptationSelector), whose per-case plan the adaptation reads once (BareReborrowLayer).
    private static bool StoresExclusiveReference(Koto node, BoundType actual)
        => node.ErasedFunctionType is null && IsBarePlace(node) &&
            (actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } ||
                TryAdaptationSelector(actual, out _));

    // SPEC 15.6.3, 15.8.2: the meet of the safe borrow layers that reach a Place, `static` when none (PathReborrowOrigin's walk, ρ for a
    // root reached in a Closure body included).
    private BoundOrigin ReachOf(Koto place) => this.PathReborrowOrigin(place, BoundOrigin.Static) ?? BoundOrigin.Static;

    // SPEC 3.5.1, 8.14, 7.7.2, 8.1.2, 15.6.3 (DECISIONS BU-27; PLAN G254): the same-Semantics Reborrow that a bare acquisition of a Place
    // storing `stored` makes, by its per-case plan, in two steps: the Origin of the layer it Reborrows (BareReborrowLayer), then its Type
    // (ReborrowedType). The layer is a stored `uniq/T` or `objuniq/T` whose plan Reborrows (the caller's BareAcquire), or a pair layer whose
    // plan (BareAcquisition) Reborrows the layer itself in an exclusive admitted case, its outer-Origin slot. Limits: the owner case's
    // Reborrow of a nested layer (PLAN G254, O1) and a family (OuterOrigin has no family row) name no layer. Null when none.
    private BoundOrigin? BareReborrowLayer(BoundType stored, Koto node)
        => stored is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } ? stored.Origin
            : this.BareAcquisition(stored, node, out var reborrow) == SemanticsMask.None && (reborrow & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0
                ? this.OuterOrigin(stored) : null;

    // The Reborrow's Type (BareReborrowLayer): the stored Type with that layer's Origin met with `reach`, the layers that reach the Place (a
    // body Place's, ReachOf; a nested capture entry's enclosing receiver, EnvironmentContainment), as each such case's Reborrow of its
    // concrete reference is. A pair slot exists only in the borrow cases (PairSlotCondition), and a case that Copies keeps the met slot,
    // which no verdict of it needs longer (SPEC 8.15.2). The stored Type itself when `reach` shortens nothing (the original `s/T` states its
    // `o` implicitly, OuterOrigin).
    private BoundType ReborrowedType(BoundType stored, BoundOrigin layer, BoundOrigin reach)
        => this.Meet(layer, reach) is var met && ReferenceEquals(met, layer) ? stored : this.WithOrigins(stored, met, (BoundOrigin[])stored.OriginArguments);

    // SPEC 3.5.1: the bare acquisition of a Place storing an exclusive reference Reborrows it in its own Semantics, exactly as an
    // annotation of the same complete Type would; the Reborrow's Type is Binding's name for it (DECISIONS BU-27), which a join's common
    // Type takes from each such source. Returns that Type, null when the node is acquired otherwise.
    private BoundType? StoredReborrow(Koto node, BoundType? actual)
    {
        if (actual is not null && StoresExclusiveReference(node, actual) && IsBareAcquisitionPosition(node) &&
            this.ExpectedAdaptation(node, actual, actual) is { } reborrow)
        {
            this.adaptations[node] = reborrow;
            return reborrow.Type;
        }

        return null;
    }
}
