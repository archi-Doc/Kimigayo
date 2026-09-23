// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BoundType? BindAbort(UnaryKoto syntax, BindingScope scope)
    {
        // The reserved builtin is independent of any user function called abort.
        if (syntax.Operand is not InvocationKoto { Method: IdentifierNameKoto { IdentifierName: "abort" }, ArgumentNodes.Count: 1 } call ||
            call.GetArgumentLabel(0) is not null)
        {
            return Fail(syntax, BindingFailure.Unsupported);
        }

        var argument = call.ArgumentNodes[0];
        var actual = this.RequireType(argument, scope, BoundType.String);
        if (argument.BindingState != BindingState.Resolved || actual is null)
        {
            return Complete(syntax, null);
        }

        var target = this.Library.Abort;
        call.Method.BoundSymbol = target;
        Complete(call.Method, BoundType.Never);
        call.BoundSymbol = target;
        var operations = this.argumentOperationScratch.Rent(1);
        try
        {
            operations[0] = new(argument, actual, BoundType.String, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: 0);
            ReadOnlySpan<int> mapping = stackalloc int[] { 0 };
            (call.CallStorage ??= new()).Set(target, BoundType.Never, null, mapping, ReadOnlySpan<BoundType?>.Empty, operations: operations.AsSpan(0, 1));
        }
        finally
        {
            this.argumentOperationScratch.Return(operations, clearArray: true);
        }

        Complete(call, BoundType.Never);
        return Complete(syntax, BoundType.Never);
    }
}
