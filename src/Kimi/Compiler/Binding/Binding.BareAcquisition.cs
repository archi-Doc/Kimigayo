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
    // SPEC 3.5: the positions without a fixed expected Type that acquire any Place operand by bare acquisition.
    // Arguments, assignment sources and results take their expected Type from the
    // declaration and adapt there instead; an anonymous function's expression body without a written or expected result
    // Type is a result source (SPEC 14.9.2) whose Type the bare acquisition infers.
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
            _ => false,
        };
    }

    // SPEC 3.5.1: the bare acquisition of a Place storing an exclusive reference Reborrows it in its own Semantics, exactly as an
    // annotation of the same complete Type would. Returns the Reborrow's Type, null when the node is acquired otherwise.
    private BoundType? StoredReborrow(Koto node, BoundType? actual)
    {
        if (actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components.Count: 1 } &&
            node.ErasedFunctionType is null && IsBareAcquisitionPosition(node) && IsBarePlace(node) &&
            this.ExpectedAdaptation(node, actual, actual) is { } reborrow)
        {
            this.adaptations[node] = reborrow;
            return reborrow.Type;
        }

        return null;
    }
}
