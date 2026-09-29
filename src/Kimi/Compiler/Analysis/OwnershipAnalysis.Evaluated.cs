// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    // One rule for every desugaring: source syntax is lowered once. An operand a desugaring evaluated is registered by that
    // syntax, and each EvaluatedKoto naming it reads the registered operand (with its element projection, if any) instead.
    private readonly Dictionary<Koto, (int Operand, int Projection)> evaluatedOperands = new(ReferenceEqualityComparer.Instance);

    // An unregistered source is a desugaring defect; it is never evaluated a second time as a fallback.
    private int EvaluatedOperand(EvaluatedKoto node, out int projection)
    {
        if (this.evaluatedOperands.TryGetValue(node.Source, out var evaluated))
        {
            projection = evaluated.Projection;
            return evaluated.Operand;
        }

        projection = -1;
        this.Unsupported(node);
        return -1;
    }

    // The key of a selection whose receiver the selection evaluated. SPEC 4.6.4, 4.6.9: a position or range key that is not
    // applied directly resolves against the length of that receiver, read through the synthesized call's EvaluatedKoto. A
    // sequence's written `^x` key is `length - x` against that receiver, and its integer key of another Type is converted
    // to isize, so the element access's one bounds check rejects every invalid position.
    private int SelectionKey(IndexKoto source, int receiver, int projection = -1, PlaceUseKind use = PlaceUseKind.Consume)
    {
        var key = ElementAccess.KeySyntax(source);
        if (ReferenceEquals(key, source.Right))
        {
            if (!ElementAccess.IsSequence(source.Left.BoundType))
            {
                return this.Expression(key, use);
            }

            if (key is FromEndIndexKoto fromEnd && this.compilation.Binding.RangeValueCall(fromEnd) is null)
            {
                var offset = this.Value(this.PositionPlace(fromEnd.Operand, this.Expression(fromEnd.Operand)));
                return receiver < 0 || offset < 0 ? -1 : this.SequenceValue(fromEnd, BoundType.ISize, SequenceOperation.FromEnd, receiver, projection, offset);
            }

            return this.PositionPlace(key, this.Expression(key, use));
        }

        this.evaluatedOperands[source.Left] = (receiver, projection);
        try
        {
            return this.Expression(key, use);
        }
        finally
        {
            this.evaluatedOperands.Remove(source.Left);
        }
    }
}
