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

    private bool LowerClosureErasure(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var input = Input(body, id, 0);
        var source = ValueType(body, input);
        if (operation.Kind != OwnershipOperationKind.Produce || source?.Kind != BoundTypeKind.Closure ||
            source.Symbol?.Declaration is not FunctionKoto { BoundClosure: { Receiver: SemanticsKind.Ref } closure } definition ||
            !ReferenceEquals(operation.Source.ErasedFunctionType, body.Places[operation.Place].Type) ||
            !Binding.CallableSignatureFits(closure.Signature, body.Places[operation.Place].Type) ||
            this.aggregateLayouts.Get(source) is not { NeedsDestruction: false } layout || layout.Value.Layout.Size > 8 ||
            this.functions?.GetValueOrDefault(definition) is not { ResultSlot: false } entry ||
            (body.IsReachable(id) && !this.Dominates(input, id)))
        {
            return Fail("Erasure requires an acquired, Owned, Shared inline concrete environment and supported signature.", out failure);
        }

        var start = function.Operands.Count;
        function.Operands.Add(new(EmissionOperandKind.SlotAddress, ValuePlace(body.Operations[input])));
        function.Instructions.Add(new(EmissionOpcode.EraseClosure, id, operation.Place, Callee: entry, OperandStart: start, OperandCount: 1, Aggregate: layout));
        this.AddStringFlags(function, operation, id);
        return true;
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

        if (closure.EnvironmentType is { } environment)
        {
            return (this.aggregateLayouts.Get(environment) is { } layout && layout.Count == closure.Captures.Count) || Fail("Capture environment layout is inconsistent.", out failure);
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
            !ReferenceEquals(body.Places[operation.Place].Type, closure.EnvironmentType ?? closure.Signature) || value.Count != closure.Captures.Count ||
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
        var environmentLayout = closure.EnvironmentType is { } environmentType ? this.aggregateLayouts.Get(environmentType) : null;
        if (closure.EnvironmentType is not null && environmentLayout is null)
        {
            return Fail("Concrete environment has no storage layout.", out failure);
        }

        var start = function.Operands.Count;
        for (var i = 0; i < value.Count; i++)
        {
            var input = Input(body, id, i);
            var capture = closure.Captures[i];
            var representation = environmentLayout?.Fields[i] ?? WindowsLowering.GetValue(capture.Environment.Type!);
            var offset = environmentLayout?.Offset(i) ?? CaptureOffset(closure, i);
            if (representation is null || offset < 0 || (environmentLayout is null && offset + representation.Layout.Size > 8) ||
                (uint)input >= (uint)id || body.Operations[input].Kind != (environmentLayout is null ? OwnershipOperationKind.Read : OwnershipOperationKind.Consume) ||
                !body.SymbolPlaces.TryGetValue(capture.Source, out var place) || place != body.Operations[input].Place ||
                !ReferenceEquals(body.Places[place].Type, capture.Environment.Type) ||
                (body.IsReachable(id) && (!this.Dominates(input, id) || (body.GetInputState(input, place) & PlaceState.MustInit) == 0)))
            {
                return Fail("Closure capture requires a checked initialized inline scalar snapshot.", out failure);
            }

            fields[i] = new(offset, representation, 0);
            function.Operands.Add(SlotTypes.IsResult(capture.Environment.Type) ? new(EmissionOperandKind.SlotAddress, body.Operations[input].Input) : this.PhysicalOperand(body, input));
        }

        function.Instructions.Add(new(EmissionOpcode.CreateClosure, id, operation.Place, Callee: callee, OperandStart: start, OperandCount: value.Count, Pattern: fields, Aggregate: environmentLayout));
        this.AddStringFlags(function, operation, id);
        return true;
    }

    private bool LowerValueCall(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundValueCall plan, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        // A monomorphized instance calls the value through its substituted signature (SPEC 21.3.1).
        var receiver = SignatureType(this, plan.ReceiverType);
        var signature = SignatureType(this, plan.Signature);
        var returnType = SignatureType(this, plan.ReturnType);
        if ((uint)operation.Input >= (uint)body.Places.Count || !ReferenceEquals(plan.Receiver, call.Method) || receiver is null || signature is null || returnType is null ||
            !ReferenceEquals(body.Places[operation.Input].Type, receiver) || !ReferenceEquals(call.BoundType, plan.ReturnType) ||
            plan.Arguments.Length != call.ArgumentNodes.Count || this.arguments.Count != call.ArgumentNodes.Count ||
            !(ScalarTypes.Supports(returnType) || SlotTypes.IsResult(returnType) || ReferenceEquals(returnType, BoundType.Unit) || ReferenceEquals(returnType, BoundType.Never)))
        {
            return Fail("Unsupported common-function call signature or receiver.", out failure);
        }

        var protectedReceiver = false;
        for (var l = 0; l < body.ComparisonLoans.Count; l++)
        {
            var loan = body.ComparisonLoans[l];
            protectedReceiver |= loan.Place == operation.Input && loan.Mode == (plan.ReceiverKind == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref) && body.HasComparisonLoan(id, l) &&
                ReferenceEquals(body.Operations[loan.Read].Source, plan.Receiver) && (!body.IsReachable(id) || this.Dominates(loan.Read, id));
        }

        if (plan.ReceiverKind == SemanticsKind.Owner)
        {
            protectedReceiver = body.Operations.Take(id).Any(x => x.Kind == OwnershipOperationKind.CallEntry && x.Place == operation.Input && ReferenceEquals(x.Source, plan.Receiver));
        }

        if (!protectedReceiver || (plan.ReceiverKind != SemanticsKind.Owner && body.IsReachable(id) && (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0))
        {
            return Fail("Common-function receiver lacks its call-wide shared Loan.", out failure);
        }

        var inputs = signature.Components[0];
        if (plan.Arguments.Length != (ReferenceEquals(inputs, BoundType.Unit) ? 0 : inputs.Components.Count))
        {
            return Fail("Common-function arguments do not match the selected signature.", out failure);
        }

        var physical = new AbiParameter[plan.Arguments.Length];
        var start = function.Operands.Count;
        var receiverType = receiver.Kind == BoundTypeKind.Semantics ? receiver.Components[0] : receiver;
        var concreteEntry = receiverType.Kind == BoundTypeKind.Closure && receiverType.Symbol?.Declaration is FunctionKoto definition ? this.functions?.GetValueOrDefault(definition) : null;
        if (concreteEntry is not null)
        {
            if (concreteEntry.ResultSlot)
            {
                if (!this.ValidateSlotCallResult(body, id, out failure))
                {
                    return false;
                }

                function.Operands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
            }

            var environment = this.aggregateLayouts.Get(receiverType)!;
            if (plan.ReceiverType.Kind == BoundTypeKind.Semantics)
            {
                var read = -1;
                foreach (var loan in body.ComparisonLoans)
                {
                    if (loan.Place == operation.Input && ReferenceEquals(loan.Callable, call))
                    {
                        read = loan.Read;
                    }
                }

                if (read < 0)
                {
                    return Fail("Borrowed concrete call has no acquired receiver address.", out failure);
                }

                function.Operands.Add(this.PhysicalOperand(body, read));
            }
            else
            {
                function.Operands.Add(environment.Value.Layout.Size == 0 ? new(EmissionOperandKind.NullAddress, 0) : new(EmissionOperandKind.SlotAddress, operation.Input));
            }
        }

        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            var argument = plan.Arguments[i];
            var entry = this.arguments[i];
            var place = body.Operations[entry].Place;
            var parameterType = SignatureType(this, argument.ParameterType);
            if (!ReferenceEquals(body.Operations[entry].Source, call) || argument.ParameterIndex != i ||
                argument.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
                parameterType is null || !ReferenceEquals(parameterType, inputs.Components[i]) ||
                !ReferenceEquals(argument.Source, call.ArgumentNodes[i]) || !ReferenceEquals(argument.SourceType, call.ArgumentNodes[i].BoundType) ||
                !ReferenceEquals(body.Places[place].Type, parameterType) || !ReferenceTypes.IsValue(parameterType) ||
                body.Values[entry].Kind != OwnershipValueKind.Alias || body.Values[entry].Count != 1 ||
                (body.IsReachable(id) && !this.Dominates(entry, id)))
            {
                return Fail("Common-function argument lacks checked value or borrow acquisition.", out failure);
            }

            physical[i] = new(WindowsLowering.GetValue(parameterType)!.ArgumentType!, string.Empty);
            function.Operands.Add(this.PhysicalOperand(body, Input(body, entry, 0)));
        }

        this.arguments.Clear();
        if (concreteEntry is not null)
        {
            function.Operands.Add(new(EmissionOperandKind.NullAddress, 0));
            function.Instructions.Add(new(EmissionOpcode.Call, id, Callee: concreteEntry, OperandStart: start, OperandCount: function.Operands.Count - start));
            return true;
        }

        var abi = new FunctionAbi(string.Empty, FunctionAbi.ResultType(returnType)!, physical, ReferenceEquals(returnType, BoundType.Never));
        function.Instructions.Add(new(EmissionOpcode.CallValue, id, operation.Input, Callee: abi, OperandStart: start, OperandCount: physical.Length));
        if (abi.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, id);
        }

        return true;
    }
}
