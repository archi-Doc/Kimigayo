// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<FunctionAbi> valueCallAbis = new();

    // A common Function value in a field or element is called through a temporary shared borrow of that part.
    private static bool IsPartReceiver(OwnershipPlace place, BoundType receiver)
        => receiver.Kind == BoundTypeKind.Function && place.Kind == OwnershipPlaceKind.Temporary &&
            place.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 } && ReferenceEquals(place.Type.Components[0], receiver);

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

    // SPEC 7.2.3: a closure inside a default of the function that declares the captured parameter reads the argument that
    // the pending call prepared for that parameter: a temporary of the calling body, or the result of the selection, `do` or
    // short-circuit join that supplied it. Any other entry reads its source binding.
    // SPEC 7.6.2, 8.10: an entry whose environment binding is initialized by a borrow of the outer binding: a Reborrow or a slot
    // borrow, or a bare entry of a pair binding whose instantiated Type is an exclusive reference (a Copy in every other instance).
    private bool BorrowingEntry(in BoundCapture capture)
        => capture.Environment.CaptureAcquisition is CaptureAcquisition.Reborrow or CaptureAcquisition.SharedSlotBorrow or CaptureAcquisition.ExclusiveSlotBorrow ||
            (capture.Environment.CaptureAcquisition == CaptureAcquisition.Bare &&
                SignatureType(this, capture.Environment.Type) is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.ObjUniq, Components.Count: 1 });

    private bool CaptureSourcePlace(OwnershipBody body, FunctionKoto closure, BindingSymbol source, int input, out int place)
    {
        if (DefaultParameters.InLaterDefault(closure, source))
        {
            place = body.Operations[input].Place;
            return (uint)place < (uint)body.Places.Count && body.Places[place].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result &&
                this.IsPreparedArgument(body, input, source, place);
        }

        return body.TrySymbolPlaceAt(source, input, out place);
    }

    // A generic Item enters the instance of its bound arguments, or the explicit specialization selected for them.
    private FunctionAbi? ClosureEntry(BoundType type, FunctionKoto definition, bool address = false)
    {
        if (type.Kind == BoundTypeKind.FunctionItem && type.Symbol?.CompilerFunction is not (null or CompilerFunctionKind.None))
        {
            return this.CompilerEntries?.Get(type);
        }

        if (type.Kind == BoundTypeKind.FunctionItem && (type.Components.Count != 0 || type.LengthArguments.Length != 0))
        {
            var entry = definition.CodeContext.Compilation.Binding.FunctionItemContext(type) is { } item && this.GenericCalls?.GetValueOrDefault(item) is { } instance
                ? instance.Selected ?? instance.Abi : null;
            return definition.IsVirtual && address ? this.Virtuals?.Address(definition, entry) : entry;
        }

        var body = type.ClosureContext is { } context ? this.GenericCalls?.GetValueOrDefault(context)?.Abi : this.functions?.GetValueOrDefault(definition);
        return definition.IsVirtual && address ? this.Virtuals?.Address(definition, body) : body;
    }

    // Retain physical signatures only; repeated compilation must not allocate
    // parameter arrays or keep a previous syntax/Binding graph alive.
    private FunctionAbi ValueCallAbi(BoundType signature, BoundType result)
    {
        ReadOnlySpan<BoundType> inputs = (BoundType[])signature.Components[0].Components;
        var resultSlot = FunctionAbi.HasResultSlot(result, this.aggregateLayouts);
        foreach (var candidate in this.valueCallAbis)
        {
            if (FunctionAbiPool.Matches(candidate, result, inputs, resultSlot, this.aggregateLayouts, callerLocation: true))
            {
                return candidate;
            }
        }

        var abi = FunctionAbiPool.Build(string.Empty, result, inputs, resultSlot, this.aggregateLayouts, callerLocation: true);
        this.valueCallAbis.Add(abi);
        return abi;
    }

    private bool LowerClosureErasure(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (body.Values[id].Count == 0)
        {
            // SPEC 7.6.4: a Function Item has no environment; the erasure adapter takes the common (environment, ret, inputs,
            // context) call and calls the function with its own ABI.
            if (operation.Kind != OwnershipOperationKind.Produce || operation.Source.BoundSymbol is not { Kind: BindingSymbolKind.Function, Declaration: FunctionKoto { IsAnonymous: false } target } ||
                body.Places[operation.Place].Type.Kind != BoundTypeKind.Function || (target.IsVirtual ? this.Virtuals?.Address(target) : this.functions?.GetValueOrDefault(target)) is not { CallerLocation: false } item)
            {
                return Fail("Function item erasure requires a resolved function entry without caller location.", out failure);
            }

            function.Instructions.Add(new(EmissionOpcode.EraseClosure, id, operation.Place, Callee: item, OperandStart: function.Operands.Count));
            return true;
        }

        var input = Input(body, id, 0);
        var source = ValueType(body, input);
        if (source?.Kind == BoundTypeKind.FunctionItem)
        {
            if (operation.Kind != OwnershipOperationKind.Produce || source.Symbol?.Declaration is not FunctionKoto itemDefinition ||
                !ReferenceEquals(body.ConcreteAt(operation.Source.ErasedFunctionType, id), body.Places[operation.Place].Type) ||
                operation.Source.CodeContext.Compilation.Binding.FunctionItemSignature(source) is not { } itemSignature ||
                !operation.Source.CodeContext.Compilation.Binding.ItemContractFits(source, itemSignature, body.Places[operation.Place].Type, operation.Source) ||
                this.ClosureEntry(source, itemDefinition, address: true) is not { } itemEntry ||
                (body.IsReachable(id) && !this.Dominates(input, id)))
            {
                return Fail("Function Item erasure requires an acquired Item and its checked signature.", out failure);
            }

            function.Instructions.Add(new(EmissionOpcode.EraseClosure, id, operation.Place, Callee: itemEntry, OperandStart: function.Operands.Count));
            return true;
        }

        if (operation.Kind != OwnershipOperationKind.Produce || source?.Kind != BoundTypeKind.Closure ||
            source.Symbol?.Declaration is not FunctionKoto { BoundClosure: { Receiver: SemanticsKind.Ref } closure } definition ||
            !ReferenceEquals(body.ConcreteAt(operation.Source.ErasedFunctionType, id), body.Places[operation.Place].Type) ||
            operation.Source.CodeContext.Compilation.Binding.ClosureSignature(source) is not { } signature ||
            !Binding.CallableSignatureFits(signature, body.Places[operation.Place].Type, definition) ||
            this.aggregateLayouts.Get(source) is not { } layout ||
            this.ClosureEntry(source, definition) is not { } entry ||
            (body.IsReachable(id) && !this.Dominates(input, id)))
        {
            return Fail("Erasure requires an acquired, Owned, Shared concrete environment and supported signature.", out failure);
        }

        var location = -1;
        if ((layout.NeedsDestruction || layout.Value.Layout.Size > 8) && !this.TryGetLocation(operation.Source, directory, constants, out location))
        {
            return Fail("Heap closure erasure requires its source location.", out failure);
        }

        var start = function.Operands.Count;
        function.Operands.Add(new(EmissionOperandKind.SlotAddress, ValuePlace(body.Operations[input])));
        function.Instructions.Add(new(EmissionOpcode.EraseClosure, id, operation.Place, Constant: location, Callee: entry, OperandStart: start, OperandCount: 1, Aggregate: layout));
        this.AddStringFlags(function, operation, id);
        return true;
    }

    private bool LowerCapture(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var value = body.Values[id];
        if (body.Function.BoundClosure is not { } closure || value.Constant < 0 || value.Constant >= closure.Captures.Count ||
            body.Operations[id].Kind != OwnershipOperationKind.Produce || !ReferenceEquals(body.Operations[id].Source, body.Function) ||
            !ReferenceEquals(ValueType(body, id), SignatureType(this, closure.Captures[(int)value.Constant].Environment.Type)) ||
            !body.SymbolPlaces.TryGetValue(closure.Captures[(int)value.Constant].Environment, out var place) || place != body.Operations[id].Place)
        {
            return Fail("Invalid closure capture parameter.", out failure);
        }

        if (closure.EnvironmentType is { } environment)
        {
            return (this.aggregateLayouts.Get(SignatureType(this, environment)!) is { } layout && layout.Count == closure.Captures.Count) || Fail("Capture environment layout is inconsistent.", out failure);
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
            !ReferenceEquals(body.Places[operation.Place].Type, body.ConcreteAt(closure.EnvironmentType ?? closure.Signature, id)) || value.Count != closure.Captures.Count ||
            this.ClosureEntry(body.Places[operation.Place].Type, source) is not { } callee ||
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
        var environmentLayout = closure.EnvironmentType is { } environmentType ? this.aggregateLayouts.Get(body.ConcreteAt(environmentType, id)!) : null;
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
            var borrowed = this.BorrowingEntry(capture);
            var place = -1;
            if (representation is null || offset < 0 || (environmentLayout is null && offset + representation.Layout.Size > 8) ||
                (uint)input >= (uint)id || body.Operations[input].Kind != (environmentLayout is null ? OwnershipOperationKind.Read : OwnershipOperationKind.Consume) ||
                (borrowed ? environmentLayout is null || body.Places[place = body.Operations[input].Place].Kind != OwnershipPlaceKind.Temporary
                    : !this.CaptureSourcePlace(body, source, capture.Source, input, out place) || place != body.Operations[input].Place) ||
                !ReferenceEquals(body.Places[place].Type, body.ConcreteAt(capture.Environment.Type, id)) ||
                (body.IsReachable(id) && (!this.Dominates(input, id) || (body.GetInputState(input, place) & PlaceState.MustInit) == 0)))
            {
                return Fail("Closure capture requires a checked initialized inline scalar snapshot.", out failure);
            }

            function.PatternSteps.Add(new(offset, representation, 0));
            function.Operands.Add(SlotTypes.IsResult(body.Places[place].Type) ? new(EmissionOperandKind.SlotAddress, body.Operations[input].Input) : this.PhysicalOperand(body, input));
        }

        function.Instructions.Add(new(EmissionOpcode.CreateClosure, id, operation.Place, Callee: callee, OperandStart: start, OperandCount: value.Count, PatternStart: patternStart, PatternCount: value.Count, Aggregate: environmentLayout));
        this.AddStringFlags(function, operation, id);
        return true;
    }

    private bool LowerValueCall(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundValueCall plan, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        // A monomorphized instance calls the value through its substituted signature (SPEC 21.3.1).
        var receiver = SignatureType(this, plan.ReceiverType);
        var signature = SignatureType(this, plan.Signature);
        var returnType = SignatureType(this, plan.ReturnType);
        if ((uint)operation.Input >= (uint)body.Places.Count || !ReferenceEquals(plan.Receiver, call.Method) || receiver is null || signature is null || returnType is null ||
            !(ReferenceEquals(body.Places[operation.Input].Type, receiver) || IsPartReceiver(body.Places[operation.Input], receiver)) || !ReferenceEquals(ElementAccess.PlaceCallReference(call) ?? call.BoundType, plan.ReturnType) ||
            plan.Arguments.Length != call.ArgumentNodes.Count ||
            !(ScalarTypes.Supports(returnType) || ReferenceTypes.IsPointer(returnType) || ReferenceTypes.IsBorrow(returnType) || SlotTypes.IsResult(returnType) || ReferenceEquals(returnType, BoundType.Unit) || ReferenceEquals(returnType, BoundType.Never)))
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
                !call.CodeContext.Compilation.Binding.FitsVerifiedTypeAt(body.Places[place].Type, parameterType, call))
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
        var itemCall = receiverType.Kind == BoundTypeKind.FunctionItem;
        var concreteEntry = receiverType.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem && receiverType.Symbol?.Declaration is FunctionKoto definition ? this.ClosureEntry(receiverType, definition) : null;
        if (itemCall && concreteEntry is null)
        {
            return Fail("Function Item call requires its resolved declaration entry.", out failure);
        }

        if (receiverType.Kind == BoundTypeKind.Closure && concreteEntry is null)
        {
            return Fail("Concrete closure call requires its resolved environment entry.", out failure);
        }

        var abi = concreteEntry ?? this.ValueCallAbi(signature, returnType);

        // A concrete closure body takes its environment before the result slot (FunctionAbiPool.Get); a common value call
        // receives the environment from the value itself, whose address leads the operands when a reference holds it.
        var borrowed = !itemCall && body.Places[operation.Input].Type.Kind == BoundTypeKind.Semantics;
        if (!itemCall && (concreteEntry is not null || borrowed))
        {
            if (borrowed)
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
                    return Fail("Borrowed value call has no acquired receiver address.", out failure);
                }

                function.Operands.Add(this.PhysicalOperand(body, read));
            }
            else
            {
                var environment = this.aggregateLayouts.Get(receiverType)!;
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

        var invocationLocation = -1;
        foreach (var physical in abi.Parameters)
        {
            if (physical.Kind is AbiParameterKind.Environment or AbiParameterKind.Context or AbiParameterKind.ResultSlot)
            {
                continue;
            }

            if (physical.Kind is AbiParameterKind.Location or AbiParameterKind.LocationLength)
            {
                if (!function.Abi.CallerLocation && invocationLocation < 0 && !this.TryGetLocation(call, directory, constants, out invocationLocation))
                {
                    return Fail("A value call requires its invocation location.", out failure);
                }

                function.Operands.Add(new(
                    function.Abi.CallerLocation
                        ? physical.Kind == AbiParameterKind.Location ? EmissionOperandKind.CallerLocation : EmissionOperandKind.CallerLocationLength
                        : physical.Kind == AbiParameterKind.Location ? EmissionOperandKind.ConstantAddress : EmissionOperandKind.ConstantLength,
                    invocationLocation));
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
        if (concreteEntry is not null && !itemCall)
        {
            function.Operands.Add(new(EmissionOperandKind.NullAddress, 0));
        }

        if (function.Operands.Count - start != abi.Parameters.Length + (concreteEntry is null && borrowed ? 1 : 0))
        {
            return Fail("Common-function physical arguments do not match its ABI.", out failure);
        }

        if (concreteEntry is not null)
        {
            var virtualSlot = -1;
            var virtualReceiver = -1;
            if (receiverType is { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto { IsVirtual: true } original } &&
                (this.Virtuals is null || !this.Virtuals.TrySlot(original, concreteEntry, out virtualSlot, out virtualReceiver)))
            {
                return Fail("A virtual Item call has no physical slot and receiver mapping.", out failure);
            }

            function.Instructions.Add(new(virtualSlot < 0 ? EmissionOpcode.Call : EmissionOpcode.VirtualCall, id, Place: virtualReceiver, Constant: virtualSlot, Callee: concreteEntry, OperandStart: start, OperandCount: function.Operands.Count - start));
        }
        else
        {
            function.Instructions.Add(new(EmissionOpcode.CallValue, id, operation.Input, Callee: abi, OperandStart: start, OperandCount: function.Operands.Count - start));
        }

        if (abi.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, id);
        }
        else if (plan.ReceiverKind == SemanticsKind.Owner &&
            (receiverType.Kind == BoundTypeKind.Function || receiverType is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { BoundClosure.Receiver: not SemanticsKind.Owner } }) &&
            this.aggregateLayouts.Get(receiverType) is { NeedsDestruction: true } cleanup)
        {
            // The owning Callable contract transfers the whole receiver. A Shared/Exclusive concrete entry (or
            // common Function entry) only borrows its environment, so this adapter retains the final destruction
            // responsibility. A Consuming concrete entry already destroys its own remaining captures.
            if (!this.TryGetLocation(call, directory, constants, out var location))
            {
                return Fail("Consuming callable cleanup has no source location.", out failure);
            }

            AddOwnedDestruction(function, id, new(EmissionOperandKind.SlotAddress, operation.Input), location, cleanup);
        }

        return true;
    }
}
