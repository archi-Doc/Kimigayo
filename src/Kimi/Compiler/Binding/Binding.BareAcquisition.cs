// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Bare acquisition (SPEC 3.5, 3.5.1, 10.2, 14.2.2): a position without a fixed expected Type acquires its operand by bare acquisition.
// - Responsibility: the positions that do (IsBareAcquisitionPosition, a Place in Discard Context among them) and the Reborrow of a Place
//   storing an exclusive reference there (StoredReborrow), recorded as the node's one adaptation, exactly as an annotation of the same
//   complete Type would record it.
// - Inputs: the node, its bound Type, its Place (IsBarePlace), its position and Discard Context (KotoHelper.IsValueContext), and the
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
    // common Type is chosen from the Types its sources take here. A Place in Discard Context (SPEC 14.2.2: a standalone expression, an
    // indented body's direct expression, a discarding single-item body) is acquired as at a value position, and discarded.
    private static bool IsBareAcquisitionPosition(Koto node)
    {
        var target = Grouped(node);
        return target.Parent switch
        {
            VariableKoto variable => ReferenceEquals(variable.InitializerKoto, target),
            DiscardKoto discard => ReferenceEquals(discard.Operand, target),
            TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto => true,
            FunctionKoto { IsAnonymous: true } function => ReferenceEquals(function.ExpressionBody, target),
            JumpKoto jump => ReferenceEquals(jump.Expression, target),
            _ => !KotoHelper.IsValueContext(target) || (ResultTarget(target) is IfKoto or MatchKoto or DoKoto && KotoHelper.IsValueContext(target)),
        };
    }

    // The outermost group around `node`, the node its consumer takes (DECISIONS R4-M145): IsBareAcquisitionPosition, ClosureEffects'
    // ConsumedByValue and the HIR publisher's discard rows read this one rule.
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

    // SPEC 3.5.1, 10.2.2: whether a node is a bare Place storing an exclusive reference, `uniq/T` or `objuniq/T`, which its bare acquisition
    // Reborrows in its own Semantics, at a bare acquisition position (StoredReborrow) and at a fixed expected Type it fits alike.
    private static bool StoresExclusiveReference(Koto node, BoundType actual)
        => actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 } &&
            node.ErasedFunctionType is null && IsBarePlace(node);

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
