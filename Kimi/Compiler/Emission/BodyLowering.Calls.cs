// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<EmissionOperand> callOperands = new();
    private Dictionary<FunctionKoto, FunctionAbi>? functions;
    private ControlFlowAnalysis? flow;
    private int[] parameterArguments = [];

    internal void ClearFunctionContext()
    {
        this.functions = null;
        this.flow = null;
        this.arguments.Clear();
    }

    private bool LowerCall(CoreIntrinsics core, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (operation.Source is not InvocationKoto { AttributeChain: null, BoundCall: { } plan } call ||
            plan.Receiver is not null || plan.TypeArguments.Length != 0 || plan.Origins.Length != 0 ||
            plan.Target.Declaration is not FunctionKoto target || plan.ArgumentOperations.Length != call.ArgumentNodes.Count ||
            plan.ArgumentToParameter.Length != call.ArgumentNodes.Count || call.ArgumentNodes.Count != target.Parameters.Count ||
            !ReferenceEquals(call.BoundType, plan.ReturnType) || !ReferenceEquals(plan.ReturnType, target.BoundSymbol?.Type))
        {
            return Fail("A call needs unsupported callee, argument acquisition or result lowering.", out failure);
        }

        var runtime = ReferenceEquals(plan.Target, core.WriteLine);
        var callee = runtime ? WindowsLowering.GetCompilerFunction(plan.Target.CompilerFunction) : this.functions!.GetValueOrDefault(target);
        if (callee is null)
        {
            return Fail("Call target has no selected implementation ABI.", out failure);
        }

        Grow(ref this.parameterArguments, target.Parameters.Count);
        this.parameterArguments.AsSpan(0, target.Parameters.Count).Fill(-1);
        var cursor = 0;
        var complete = true;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var parameter = plan.ArgumentToParameter[i];
            var acquisition = plan.ArgumentOperations[i];
            if ((uint)parameter >= (uint)target.Parameters.Count || this.parameterArguments[parameter] != -1 ||
                acquisition.Kind != ArgumentOperationKind.Value || acquisition.ParameterIndex != parameter ||
                !ReferenceEquals(acquisition.ParameterType, target.Parameters[parameter].Type.BoundType))
            {
                return Fail("Invalid call argument mapping or acquisition.", out failure);
            }

            // The builder omits CallEntry for an argument whose evaluation cannot complete.
            // It still checks the rest of the source and this call's signature.
            if (!this.flow!.Nodes[call.ArgumentNodes[i]].CanCompleteNormally)
            {
                this.parameterArguments[parameter] = -2;
                complete = false;
                continue;
            }

            if (cursor == this.arguments.Count)
            {
                return Fail("Missing acquired call argument.", out failure);
            }

            var entry = this.arguments[cursor++];
            var place = body.Operations[entry].Place;
            var type = body.Places[place].Type;
            if (!ReferenceEquals(body.Operations[entry].Source, call) || !ReferenceEquals(type, acquisition.ParameterType))
            {
                return Fail("Call entry does not match its argument Type or call.", out failure);
            }

            if (IsScalar(type) && (body.Values[entry].Kind != OwnershipValueKind.Alias || body.Values[entry].Count != 1 ||
                ValuePlace(body.Operations[Input(body, entry, 0)]) != place ||
                (body.IsReachable(entry) && !this.Dominates(Input(body, entry, 0), entry))))
            {
                return Fail("Call argument value is unavailable at acquisition.", out failure);
            }

            if (ReferenceEquals(type, BoundType.String) && (!this.IsStringValue(body.Places[place]) ||
                (body.IsReachable(entry) && (body.GetInputState(entry, place) & PlaceState.MustInit) == 0)))
            {
                return Fail("Owned string argument is not an initialized acquired value.", out failure);
            }

            this.parameterArguments[parameter] = entry;
        }

        if (cursor != this.arguments.Count || (!complete && body.IsReachable(id)))
        {
            return Fail("Call has extra arguments or follows a noncompleting argument.", out failure);
        }

        this.arguments.Clear();
        if (!complete)
        {
            return true;
        }

        this.callOperands.Clear();
        var location = -1;
        for (var i = 0; i < callee.Parameters.Length; i++)
        {
            var physical = callee.Parameters[i];
            if (physical.Kind == AbiParameterKind.ResultSlot)
            {
                if (!ReferenceEquals(plan.ReturnType, BoundType.String) || !this.ValidateStringCallResult(body, id, out failure))
                {
                    return false;
                }

                this.callOperands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
                continue;
            }

            if (physical.Kind is AbiParameterKind.Location or AbiParameterKind.LocationLength)
            {
                if (!runtime || (location < 0 && !this.TryGetLocation(call, directory, constants, out location)))
                {
                    return Fail("A runtime call has no diagnostic source location.", out failure);
                }

                this.callOperands.Add(new(physical.Kind == AbiParameterKind.Location ? EmissionOperandKind.ConstantAddress : EmissionOperandKind.ConstantLength, location));
                continue;
            }

            if ((uint)physical.LogicalIndex >= (uint)target.Parameters.Count)
            {
                return Fail("Physical parameter has no logical argument.", out failure);
            }

            var entry = this.parameterArguments[physical.LogicalIndex];
            var type = target.Parameters[physical.LogicalIndex].Type.BoundType!;
            if (body.IsReachable(id) && !this.Dominates(entry, id))
            {
                return Fail("Call argument does not dominate the call.", out failure);
            }

            if (physical.Kind == AbiParameterKind.Value && IsScalar(type))
            {
                var value = Input(body, entry, 0);
                if (body.IsReachable(id) && !this.Dominates(value, id))
                {
                    return Fail("Call argument does not dominate the call.", out failure);
                }

                this.callOperands.Add(this.PhysicalOperand(body, value));
            }
            else if (physical.Kind == AbiParameterKind.OwnedSlot && ReferenceEquals(type, BoundType.String))
            {
                if (callee.ResultSlot && body.Operations[entry].Place == operation.Place)
                {
                    return Fail("A call cannot share its argument and result storage.", out failure);
                }

                this.callOperands.Add(new(EmissionOperandKind.SlotAddress, body.Operations[entry].Place));
            }
            else
            {
                return Fail("Unsupported physical call argument.", out failure);
            }
        }

        var expectedResult = FunctionAbi.ResultType(plan.ReturnType);
        if (expectedResult != callee.Result || callee.NoReturn != ReferenceEquals(plan.ReturnType, BoundType.Never) ||
            callee.ResultSlot != ReferenceEquals(plan.ReturnType, BoundType.String) ||
            this.callOperands.Count != callee.Parameters.Length ||
            (IsScalar(plan.ReturnType) && (body.Values[id].Kind != OwnershipValueKind.Call || !ReferenceEquals(ValueType(body, id), plan.ReturnType))))
        {
            return Fail("Call result or argument plan does not match its physical ABI.", out failure);
        }

        function.AddCall(id, callee, CollectionsMarshal.AsSpan(this.callOperands));
        if (callee.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, id);
        }

        return true;
    }
}
