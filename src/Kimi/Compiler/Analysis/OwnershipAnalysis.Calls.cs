// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    // Publish declared result ancestry while the selected declaration and parameter mapping are available.
    // Equal instantiated Origins never choose between two independently acquired caller Loans.
    private void RecordCall(int operation, int start, CallPlan plan)
    {
        var count = this.body.CallInputCount - start;
        var source = CallResultSource.None;
        var receiver = -1;
        var inputSlot = -1;
        var oneInput = true;
        var receiverOnly = true;
        var named = false;
        if (plan.Target is { CompilerFunction: CompilerFunctionKind.None, Declaration: FunctionKoto target })
        {
            source = CallResultSource.InputOrigins;
            if (target.ReturnType?.BoundType is { } result)
            {
                VisitType(result);
            }

            if (oneInput && inputSlot >= 0)
            {
                source = CallResultSource.Input;
            }
            else if (plan.Receiver is not null && receiverOnly && named)
            {
                source = CallResultSource.ReceiverOrigins;
            }

            foreach (var input in this.body.CallInputStorage.Slice(start, count))
            {
                if ((source == CallResultSource.Input && count == target.Parameters.Count && !input.IsDefault && input.Parameter == inputSlot) ||
                    (source == CallResultSource.ReceiverOrigins && input.Parameter == plan.ReceiverOperation.ParameterIndex))
                {
                    receiver = input.Entry;
                    break;
                }
            }

            void VisitType(BoundType type)
            {
                if (type.Origin is { } origin)
                {
                    VisitOrigin(origin);
                }

                for (var i = 0; i < type.OriginArguments.Count; i++)
                {
                    VisitOrigin(type.OriginArguments[i]);
                }

                for (var i = 0; i < type.Components.Count; i++)
                {
                    VisitType(type.Components[i]);
                }
            }

            void VisitOrigin(BoundOrigin origin)
            {
                if (origin.Kind != OriginKind.Static)
                {
                    named = true;
                    receiverOnly &= origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, target.BoundSymbol?.Scope.Owner);
                }

                if (origin.Kind == OriginKind.Intersection)
                {
                    for (var i = 0; i < origin.Operands.Count; i++)
                    {
                        VisitOrigin(origin.Operands[i]);
                    }
                }
                else if (origin.Kind == OriginKind.Input && ReferenceEquals(origin.Binder, target))
                {
                    oneInput &= inputSlot < 0 || inputSlot == origin.Slot;
                    inputSlot = origin.Slot;
                }
            }
        }

        this.body.RecordCall(operation, new(start, count, plan, source, receiver));
    }
}
