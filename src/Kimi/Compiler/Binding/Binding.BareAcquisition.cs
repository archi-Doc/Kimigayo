// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Bare acquisition (SPEC 3.5, 3.5.1, 10.2): a position without a fixed expected Type acquires its operand by bare acquisition.
// - Responsibility: the positions that do (IsBareAcquisitionPosition) and the Reborrow of a Place storing an exclusive reference there
//   (StoredReborrow), recorded as the node's one adaptation, exactly as an annotation of the same complete Type would record it.
// - Inputs: the node, its bound Type, its Place (IsBarePlace) and the common adaptation (ExpectedAdaptation), which names the Reborrow.
// - Outputs: the recorded adaptation and its Type; the bare entries of pair layers per Semantics case are Binding.PairCases'.
// - Invariants: a position with a fixed expected Type adapts by the common adaptation instead.
// - Prohibitions: no consumer decides by its own position test whether a bare acquisition Reborrows.
public sealed partial class Binding
{
    // SPEC 3.5, 10.2: the positions without a fixed expected Type that acquire any Place operand by bare acquisition.
    // Arguments, assignment sources and results take their expected Type from the declaration and adapt there instead. A result source
    // (SPEC 14.9.1, 14.9.2) without a fixed Target Result Type is one: an anonymous function's expression body without a written or
    // expected result Type, a jump's operand, and a selection's or `do`'s body when its value is used (ResultTarget); the join's
    // common Type is chosen from the Types its sources take here.
    private static bool IsBareAcquisitionPosition(Koto node)
    {
        var target = node;
        while (target.Parent is ParenthesizedKoto parenthesized)
        {
            target = parenthesized;
        }

        return target.Parent switch
        {
            VariableKoto variable => ReferenceEquals(variable.InitializerKoto, target),
            DiscardKoto discard => ReferenceEquals(discard.Operand, target),
            TupleLiteralKoto or ArrayLiteralKoto => true,
            FunctionKoto { IsAnonymous: true } function => ReferenceEquals(function.ExpressionBody, target),
            JumpKoto jump => ReferenceEquals(jump.Expression, target),
            _ => ResultTarget(target) is IfKoto or MatchKoto or DoKoto && KotoHelper.IsValueContext(target),
        };
    }

    // SPEC 3.5.1: the bare acquisition of a Place storing an exclusive reference, `uniq/T` or `objuniq/T`, Reborrows it in its own
    // Semantics, exactly as an annotation of the same complete Type would; the Reborrow's Type is Binding's name for it (DECISIONS
    // BU-27), which a join's common Type takes from each such source. Returns that Type, null when the node is acquired otherwise.
    private BoundType? StoredReborrow(Koto node, BoundType? actual)
    {
        if (actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } &&
            node.ErasedFunctionType is null && IsBareAcquisitionPosition(node) && IsBarePlace(node) &&
            this.ExpectedAdaptation(node, actual, actual) is { } reborrow)
        {
            this.adaptations[node] = reborrow;
            return reborrow.Type;
        }

        return null;
    }
}
