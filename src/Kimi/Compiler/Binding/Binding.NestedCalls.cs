// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly HashSet<InvocationKoto> waitingNestedCalls = new(ReferenceEqualityComparer.Instance);
    private InvocationKoto? nestedArgumentProbe;

    private static bool WasWaitingNestedCall(Koto node)
        => KotoHelper.UnwrapParentheses(node) is InvocationKoto call && call.CodeContext.Compilation.Binding.waitingNestedCalls.Contains(call);

    private static bool IsWaitingNestedCall(Koto node) => node.BoundType is null && WasWaitingNestedCall(node);

    // SPEC 10.5: first obtain independent evidence. Only an inner call that would need a result expectation waits; failures of
    // its own inputs or declarations are still diagnosed here. Parentheses preserve the same boundary.
    private BoundType? BindIndependentArgument(Koto argument, BindingScope scope)
    {
        var previous = this.nestedArgumentProbe;
        this.nestedArgumentProbe = KotoHelper.UnwrapParentheses(argument) as InvocationKoto;
        try
        {
            return this.BindNode(argument, scope);
        }
        finally
        {
            this.nestedArgumentProbe = previous;
        }
    }

    private bool DeferNestedCall(InvocationKoto call, BoundType? expected)
    {
        if (expected is not null || !ReferenceEquals(this.nestedArgumentProbe, call))
        {
            return false;
        }

        this.waitingNestedCalls.Add(call);
        return true;
    }
}
