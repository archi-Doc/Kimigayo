// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int ArithmeticUpdate(BinaryKoto source, InvocationKoto call)
    {
        if (call.BoundCall is not { ArgumentOperations.Length: 2 } plan)
        {
            this.Unsupported(source);
            return -1;
        }

        var target = KotoHelper.UnwrapParentheses(source.Left);
        var destination = target is IdentifierNameKoto ? this.Local(target) : -1;
        if (destination < 0)
        {
            this.Unsupported(source);
            return -1;
        }

        var depth = this.comparisonDepth++;
        var right = this.PrepareCallArgument(call, source.Right, plan.ArgumentOperations[1]);
        var left = this.PrepareCallArgument(call, source.Left, plan.ArgumentOperations[0]);
        // Preparation is RHS-first; the retained requirement still receives operands in their original positions.
        var updated = this.Call(call, preparedArguments: [left, right]);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        if (updated < 0)
        {
            return -1;
        }

        this.Emit(OwnershipOperationKind.Write, source, destination, updated);
        return this.Temporary(source);
    }
}
