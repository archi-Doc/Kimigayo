// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A selected operator shares call acquisition, Origin solving and witness instantiation without rewriting parents.
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
        callee.BindingFailure = BindingFailure.None;
        arguments.CopyTo((Koto[])call.ArgumentNodes);
        call.BindingState = BindingState.Unvisited;
        call.BindingFailure = BindingFailure.None;
        call.BoundMeaning = null;
        call.BoundSymbol = null;
        call.CallStorage = callee.RequirementStorage;
        this.nodes.Add(call);
        this.BindCall(call, scope, null);
        if (call.BoundCall is not { } selected)
        {
            return call;
        }

        callee.RequirementStorage = selected;
        selected.TupleOperator = tupleOperator;
        // Fixed source Origins affect fitting and Loans, but do not require a generic Type instance.
        if (!self.ContainsParameter)
        {
            var resolved = this.InstantiateRequirementCall(selected, selected, callee.ImplementationStorage);
            if (resolved is null)
            {
                this.Fail(call, BindingFailure.Unsupported, true);
                return call;
            }

            call.CallStorage = resolved;
            callee.ImplementationStorage = resolved;
        }

        return call;
    }
}
