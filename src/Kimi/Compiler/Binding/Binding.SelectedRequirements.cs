// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A selected operator shares call acquisition and Origin solving without rewriting parents; its call is the requirement's.
    private InvocationKoto BindSelectedRequirement(Koto root, InvocationKoto? call, ReadOnlySpan<Koto> arguments, BoundType self, BindingSymbol contract, BindingSymbol requirement, BindingScope scope, bool tupleOperator = false)
    {
        if (call is null || !call.Span.Equals(root.Span) || call.ArgumentNodes.Count != arguments.Length)
        {
            var target = new RequirementCalleeKoto(root) { Parent = root };
            call = new(root, target, new Koto[arguments.Length]);
        }

        var callee = (RequirementCalleeKoto)call.Method;
        callee.Self = self;
        callee.Contract = contract;
        callee.BoundSymbol = requirement;
        callee.BoundType = requirement.Type;
        callee.BindingState = BindingState.Resolved;
        this.ClearFailure(callee);
        arguments.CopyTo((Koto[])call.ArgumentNodes);
        call.BindingState = BindingState.Unvisited;
        this.ClearFailure(call);
        call.BoundMeaning = null;
        call.BoundSymbol = null;
        call.CallStorage = callee.RequirementStorage;
        this.BindSynthesizedCall(root, call, scope);
        if (this.CallOf(call) is not { } selected)
        {
            return call;
        }

        callee.RequirementStorage = selected;
        selected.TupleOperator = tupleOperator;
        if (!self.HasAbstractPart)
        {
            this.ProjectReceiver(selected, self, root); // SPEC 8.15: the call stays the requirement's; its witness's receiver is the base's.
        }

        return call;
    }
}
