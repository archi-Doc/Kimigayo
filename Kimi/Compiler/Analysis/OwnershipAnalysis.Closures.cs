// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int CreateClosure(FunctionKoto source)
    {
        var closure = source.BoundClosure!;
        var mark = this.arguments.Count;
        for (var i = 0; i < closure.Captures.Count; i++)
        {
            var capture = closure.Captures[i];
            if (!this.body.SymbolPlaces.TryGetValue(capture.Source, out var place))
            {
                this.Unsupported(source);
                return -1;
            }

            var read = this.Emit(OwnershipOperationKind.Read, source, place);
            this.arguments.Add(read);
        }

        var result = this.Temporary(source);
        this.SetValue(this.Value(result), OwnershipValueKind.Closure, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.arguments).Slice(mark));
        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        return result;
    }

    private int CallValue(InvocationKoto call, BoundValueCall plan)
    {
        var depth = this.comparisonDepth++;
        var receiver = this.Expression(plan.Receiver, PlaceUseKind.Read);
        if (receiver < 0)
        {
            this.comparisonDepth = depth;
            return -1;
        }

        // Even a temporary has a distinct read marking the receiver Loan's start.
        this.Emit(OwnershipOperationKind.Read, plan.Receiver, receiver);
        this.BeginSharedLoan(receiver);
        var mark = this.arguments.Count;
        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            if (plan.Arguments[i].Kind != ArgumentOperationKind.Value)
            {
                this.Unsupported(call);
            }

            this.arguments.Add(this.Argument(call.ArgumentNodes[i], plan.Arguments[i].Kind));
        }

        var acquired = true;
        for (var i = mark; i < this.arguments.Count; i++)
        {
            if (this.arguments[i] < 0)
            {
                acquired = false;
            }
            else
            {
                this.Emit(OwnershipOperationKind.CallEntry, call, this.arguments[i]);
            }
        }

        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        var invoke = this.Emit(OwnershipOperationKind.Call, call, input: receiver);
        this.Connect(invoke, this.abortExit, OwnershipEdgeKind.Abort);
        if (!acquired || ReferenceEquals(plan.ReturnType, BoundType.Never))
        {
            this.current = -1;
            this.BeginChecking(invoke);
            this.EndComparisonLoans(depth, call);
            this.comparisonDepth = depth;
            return -1;
        }

        var result = this.Temporary(call);
        this.body.OperationStorage[invoke] = this.body.Operations[invoke] with { Place = result };
        if (ScalarResult(plan.ReturnType))
        {
            this.SetValue(invoke, OwnershipValueKind.Call, []);
            this.SetValue(this.Value(result), OwnershipValueKind.Alias, [invoke]);
        }

        this.EndComparisonLoans(depth, call);
        this.comparisonDepth = depth;
        return result;
    }
}
