// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static int CaptureOffset(BoundClosure closure, int index)
    {
        var offset = 0;
        for (var alignment = 8; alignment > 0; alignment >>= 1)
        {
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                var value = WindowsLowering.GetValue(closure.Captures[i].Environment.Type!);
                if (value is null || value.Layout.Alignment > 8)
                {
                    return -1;
                }

                if (value.Layout.Alignment != alignment)
                {
                    continue;
                }

                if (i == index)
                {
                    return offset;
                }

                offset += value.Layout.Size;
            }
        }

        return offset;
    }

    private bool LowerCapture(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var value = body.Values[id];
        if (body.Function.BoundClosure is not { } closure || value.Constant < 0 || value.Constant >= closure.Captures.Count ||
            body.Operations[id].Kind != OwnershipOperationKind.Produce || !ReferenceEquals(body.Operations[id].Source, body.Function) ||
            !ReferenceEquals(ValueType(body, id), closure.Captures[(int)value.Constant].Environment.Type) ||
            !body.SymbolPlaces.TryGetValue(closure.Captures[(int)value.Constant].Environment, out var place) || place != body.Operations[id].Place)
        {
            return Fail("Invalid closure capture parameter.", out failure);
        }

        var offset = CaptureOffset(closure, (int)value.Constant);
        var representation = WindowsLowering.GetValue(ValueType(body, id)!);
        if (offset < 0 || representation is null || offset + representation.Layout.Size > 8)
        {
            return Fail("Closure environment requires unsupported non-inline storage.", out failure);
        }

        function.AddScalar(EmissionOpcode.PatternRead, id, [new(EmissionOperandKind.EnvironmentAddress, 0), new(EmissionOperandKind.Integer, offset)], representation: representation);
        return true;
    }

    private bool LowerClosure(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (operation.Source is not FunctionKoto { BoundClosure: { } closure } source ||
            closure.Signature.Kind != BoundTypeKind.Function ||
            !ReferenceEquals(body.Places[operation.Place].Type, closure.Signature) || value.Count != closure.Captures.Count ||
            this.functions?.GetValueOrDefault(source) is not { } callee ||
            (body.IsReachable(id) && (body.GetInputState(id, operation.Place) & PlaceState.MayInit) != 0))
        {
            return Fail("Invalid closure creation or selected entry.", out failure);
        }

        var parameters = closure.Signature.Components[0];
        var count = ReferenceEquals(parameters, BoundType.Unit) ? 0 : parameters.Components.Count;
        if (source.Parameters.Count != count || !ReferenceEquals(source.BoundSymbol?.Type, closure.Signature.Components[1]))
        {
            return Fail("Closure entry does not match its common-function signature.", out failure);
        }

        for (var i = 0; i < count; i++)
        {
            if (!ReferenceEquals(source.Parameters[i].Type.BoundType, parameters.Components[i]))
            {
                return Fail("Closure entry parameter does not match its common-function signature.", out failure);
            }
        }

        var fields = new PatternTestStep[value.Count];
        var start = function.Operands.Count;
        for (var i = 0; i < value.Count; i++)
        {
            var input = Input(body, id, i);
            var capture = closure.Captures[i];
            var representation = WindowsLowering.GetValue(capture.Environment.Type!);
            var offset = CaptureOffset(closure, i);
            if (representation is null || offset < 0 || offset + representation.Layout.Size > 8 ||
                (uint)input >= (uint)id || body.Operations[input].Kind != OwnershipOperationKind.Read ||
                !body.SymbolPlaces.TryGetValue(capture.Source, out var place) || place != body.Operations[input].Place ||
                !ReferenceEquals(body.Places[place].Type, capture.Environment.Type) ||
                (body.IsReachable(id) && (!this.Dominates(input, id) || (body.GetInputState(input, place) & PlaceState.MustInit) == 0)))
            {
                return Fail("Closure capture requires a checked initialized inline scalar snapshot.", out failure);
            }

            fields[i] = new(offset, representation, 0);
            function.Operands.Add(this.PhysicalOperand(body, input));
        }

        function.Instructions.Add(new(EmissionOpcode.CreateClosure, id, operation.Place, Callee: callee, OperandStart: start, OperandCount: value.Count, Pattern: fields));
        this.AddStringFlags(function, operation, id);
        return true;
    }

    private bool LowerValueCall(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundValueCall plan, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if ((uint)operation.Input >= (uint)body.Places.Count || !ReferenceEquals(plan.Receiver, call.Method) ||
            !ReferenceEquals(body.Places[operation.Input].Type, plan.Signature) || !ReferenceEquals(call.BoundType, plan.ReturnType) ||
            plan.Arguments.Length != call.ArgumentNodes.Count || this.arguments.Count != call.ArgumentNodes.Count ||
            !(ScalarTypes.Supports(plan.ReturnType) || ReferenceEquals(plan.ReturnType, BoundType.Unit) || ReferenceEquals(plan.ReturnType, BoundType.Never)))
        {
            return Fail("Unsupported common-function call signature or receiver.", out failure);
        }

        var protectedReceiver = false;
        for (var l = 0; l < body.ComparisonLoans.Count; l++)
        {
            var loan = body.ComparisonLoans[l];
            protectedReceiver |= loan.Place == operation.Input && loan.Mode == LoanRequirement.Ref && body.HasComparisonLoan(id, l) &&
                ReferenceEquals(body.Operations[loan.Read].Source, plan.Receiver) && (!body.IsReachable(id) || this.Dominates(loan.Read, id));
        }

        if (!protectedReceiver || (body.IsReachable(id) && (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0))
        {
            return Fail("Common-function receiver lacks its call-wide shared Loan.", out failure);
        }

        var inputs = plan.Signature.Components[0];
        if (plan.Arguments.Length != (ReferenceEquals(inputs, BoundType.Unit) ? 0 : inputs.Components.Count))
        {
            return Fail("Common-function arguments do not match the selected signature.", out failure);
        }

        var physical = new AbiParameter[plan.Arguments.Length];
        var start = function.Operands.Count;
        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            var argument = plan.Arguments[i];
            var entry = this.arguments[i];
            var place = body.Operations[entry].Place;
            if (!ReferenceEquals(body.Operations[entry].Source, call) || argument.ParameterIndex != i || argument.Kind != ArgumentOperationKind.Value ||
                !ReferenceEquals(argument.ParameterType, inputs.Components[i]) ||
                !ReferenceEquals(argument.Source, call.ArgumentNodes[i]) || !ReferenceEquals(argument.SourceType, call.ArgumentNodes[i].BoundType) ||
                !ReferenceEquals(body.Places[place].Type, argument.ParameterType) || !ScalarTypes.Supports(argument.ParameterType) ||
                body.Values[entry].Kind != OwnershipValueKind.Alias || body.Values[entry].Count != 1 ||
                (body.IsReachable(id) && !this.Dominates(entry, id)))
            {
                return Fail("Common-function argument lacks checked scalar acquisition.", out failure);
            }

            physical[i] = new(WindowsLowering.GetValue(argument.ParameterType!)!.ArgumentType!, string.Empty);
            function.Operands.Add(this.PhysicalOperand(body, Input(body, entry, 0)));
        }

        this.arguments.Clear();
        var abi = new FunctionAbi(string.Empty, FunctionAbi.ResultType(plan.ReturnType)!, physical, ReferenceEquals(plan.ReturnType, BoundType.Never));
        function.Instructions.Add(new(EmissionOpcode.CallValue, id, operation.Input, Callee: abi, OperandStart: start, OperandCount: physical.Length));
        if (abi.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, id);
        }

        return true;
    }
}
