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

    // The key of a selection whose receiver the selection evaluated. SPEC 4.6.4: an Index or range key resolves against the
    // length of that receiver, read through the synthesized call's EvaluatedKoto.
    private int SelectionKey(IndexKoto source, int receiver, int projection = -1, PlaceUseKind use = PlaceUseKind.Consume)
    {
        var key = ElementAccess.KeySyntax(source);
        if (ReferenceEquals(key, source.Right))
        {
            return this.Expression(key, use);
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
