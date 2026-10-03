// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<FunctionAbi> valueCallAbis = new();

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

    // Retain physical signatures only; repeated compilation must not allocate
    // parameter arrays or keep a previous syntax/Binding graph alive.
    private FunctionAbi ValueCallAbi(BoundType signature, BoundType result)
    {
        ReadOnlySpan<BoundType> inputs = (BoundType[])signature.Components[0].Components;
        var resultSlot = FunctionAbi.HasResultSlot(result, this.aggregateLayouts);
        foreach (var candidate in this.valueCallAbis)
        {
            if (FunctionAbiPool.Matches(candidate, result, inputs, resultSlot, this.aggregateLayouts))
            {
                return candidate;
            }
        }

        var abi = FunctionAbiPool.Build(string.Empty, result, inputs, resultSlot, this.aggregateLayouts);
        this.valueCallAbis.Add(abi);
        return abi;
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
            this.functions?.GetValueOrDefault(definition) is not { } entry ||
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

        var patternStart = function.PatternSteps.Count;
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
            // SPEC 7.6.2: a borrowing entry consumes the temporary its borrow of the outer binding produced.
            var borrowed = capture.Environment.CaptureAcquisition is CaptureAcquisition.Reborrow or CaptureAcquisition.SharedSlotBorrow or CaptureAcquisition.ExclusiveSlotBorrow;
            var place = -1;
            if (representation is null || offset < 0 || (environmentLayout is null && offset + representation.Layout.Size > 8) ||
                (uint)input >= (uint)id || body.Operations[input].Kind != (environmentLayout is null ? OwnershipOperationKind.Read : OwnershipOperationKind.Consume) ||
                (borrowed ? environmentLayout is null || body.Places[place = body.Operations[input].Place].Kind != OwnershipPlaceKind.Temporary
                    : !body.SymbolPlaces.TryGetValue(capture.Source, out place) || place != body.Operations[input].Place) ||
                !ReferenceEquals(body.Places[place].Type, capture.Environment.Type) ||
                (body.IsReachable(id) && (!this.Dominates(input, id) || (body.GetInputState(input, place) & PlaceState.MustInit) == 0)))
            {
                return Fail("Closure capture requires a checked initialized inline scalar snapshot.", out failure);
            }

            function.PatternSteps.Add(new(offset, representation, 0));
            function.Operands.Add(SlotTypes.IsResult(capture.Environment.Type) ? new(EmissionOperandKind.SlotAddress, body.Operations[input].Input) : this.PhysicalOperand(body, input));
        }

        function.Instructions.Add(new(EmissionOpcode.CreateClosure, id, operation.Place, Callee: callee, OperandStart: start, OperandCount: value.Count, PatternStart: patternStart, PatternCount: value.Count, Aggregate: environmentLayout));
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
            plan.Arguments.Length != call.ArgumentNodes.Count ||
            !(ScalarTypes.Supports(returnType) || ReferenceTypes.IsPointer(returnType) || SlotTypes.IsResult(returnType) || ReferenceEquals(returnType, BoundType.Unit) || ReferenceEquals(returnType, BoundType.Never)))
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
            protectedReceiver = false;
            for (var entry = 0; entry < id && !protectedReceiver; entry++)
            {
                var input = body.Operations[entry];
                protectedReceiver = input.Kind == OwnershipOperationKind.CallEntry && input.Place == operation.Input && ReferenceEquals(input.Source, plan.Receiver);
            }
        }

        if (body.IsReachable(id) && (!protectedReceiver || (plan.ReceiverKind != SemanticsKind.Owner && (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0)))
        {
            return Fail("Common-function receiver lacks its call-wide shared Loan.", out failure);
        }

        var inputs = signature.Components[0];
        if (plan.Arguments.Length != (ReferenceEquals(inputs, BoundType.Unit) ? 0 : inputs.Components.Count))
        {
            return Fail("Common-function arguments do not match the selected signature.", out failure);
        }

        var complete = true;
        var cursor = 0;
        for (var i = 0; i < plan.Arguments.Length; i++)
        {
            var argument = plan.Arguments[i];
            var parameterType = SignatureType(this, argument.ParameterType);
            if (argument.ParameterIndex != i ||
                argument.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
                parameterType is null || !ReferenceEquals(parameterType, inputs.Components[i]) ||
                !ReferenceEquals(argument.Source, call.ArgumentNodes[i]) || !ReferenceEquals(argument.SourceType, call.ArgumentNodes[i].BoundType))
            {
                return Fail("Common-function argument lacks checked value or borrow acquisition.", out failure);
            }

            if (!this.flow!.Nodes[call.ArgumentNodes[i]].CanCompleteNormally)
            {
                complete = false;
                continue;
            }

            if (cursor == this.arguments.Count)
            {
                return Fail("Missing acquired common-function argument.", out failure);
            }

            var entry = this.arguments[cursor++];
            var place = body.Operations[entry].Place;
            if (!ReferenceEquals(body.Operations[entry].Source, call) ||
                !call.CodeContext.Compilation.Binding.FitsTypeAt(body.Places[place].Type, parameterType, call))
            {
                return Fail("Common-function entry does not match its argument Type or call.", out failure);
            }

            if (!this.ValidateCallArgumentStorage(body, entry, id, out failure))
            {
                return false;
            }
        }

        if (cursor != this.arguments.Count || (!complete && body.IsReachable(id)))
        {
            return Fail("Common-function call has extra arguments or follows a noncompleting argument.", out failure);
        }

        if (!complete)
        {
            this.arguments.Clear();
            function.Add(EmissionOpcode.Unreachable, id);
            return true;
        }

        for (var i = 0; i < inputs.Components.Count; i++)
        {
            if (!FunctionAbi.SupportsParameter(inputs.Components[i], this.aggregateLayouts))
            {
                return Fail("Unsupported common-function parameter representation.", out failure);
            }
        }

        if (FunctionAbi.ResultType(returnType, this.aggregateLayouts) is null)
        {
            return Fail("Unsupported common-function result representation.", out failure);
        }

        var start = function.Operands.Count;
        var receiverType = receiver.Kind == BoundTypeKind.Semantics ? receiver.Components[0] : receiver;
        var concreteEntry = receiverType.Kind == BoundTypeKind.Closure && receiverType.Symbol?.Declaration is FunctionKoto definition ? this.functions?.GetValueOrDefault(definition) : null;
        var abi = concreteEntry ?? this.ValueCallAbi(signature, returnType);

        // A concrete closure body takes its environment before the result slot (FunctionAbiPool.Get); a common value call
        // receives the environment from the value itself.
        if (concreteEntry is not null)
        {
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

        if (abi.ResultSlot)
        {
            if (!this.ValidateSlotCallResult(body, id, out failure))
            {
                return false;
            }

            function.Operands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
        }

        foreach (var physical in abi.Parameters)
        {
            if (physical.Kind is AbiParameterKind.Environment or AbiParameterKind.Context or AbiParameterKind.ResultSlot)
            {
                continue;
            }

            var parameter = physical.LogicalIndex;
            if (!this.TryCallArgumentOperand(body, this.arguments[parameter], id, inputs.Components[parameter], physical.Kind, out var operand, out failure))
            {
                return false;
            }

            function.Operands.Add(operand);
        }

        this.arguments.Clear();
        if (concreteEntry is not null)
        {
            function.Operands.Add(new(EmissionOperandKind.NullAddress, 0));
        }

        if (function.Operands.Count - start != abi.Parameters.Length)
        {
            return Fail("Common-function physical arguments do not match its ABI.", out failure);
        }

        if (concreteEntry is not null)
        {
            function.Instructions.Add(new(EmissionOpcode.Call, id, Callee: concreteEntry, OperandStart: start, OperandCount: function.Operands.Count - start));
        }
        else
        {
            function.Instructions.Add(new(EmissionOpcode.CallValue, id, operation.Input, Callee: abi, OperandStart: start, OperandCount: function.Operands.Count - start));
        }

        if (abi.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, id);
        }

        return true;
    }
}
