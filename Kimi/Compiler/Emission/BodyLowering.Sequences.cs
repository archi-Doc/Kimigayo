// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool TrySequenceComponent(BoundType receiver, Koto source, int index, out BoundType? component, out AggregateLayout? layout)
    {
        component = receiver.Components.Count == 1 ? receiver.Components[0] : null;
        layout = null;
        if (index == -1)
        {
            return component is not null;
        }

        if (index < 0 || component?.Kind != BoundTypeKind.Tuple || source is not ForKoto { IsTupleBinding: true } loop ||
            loop.Bindings.Count != component.Components.Count || (uint)index >= (uint)component.Components.Count ||
            (layout = this.aggregateLayouts.Get(component)) is null)
        {
            return false;
        }

        component = component.Components[index];
        return true;
    }

    private bool LowerSequence(KimiLibrary library, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (value.Constant < 0 || value.Constant >= body.Sequences.Count || operation.Kind != OwnershipOperationKind.Produce)
        {
            return Fail("Missing sequence value plan.", out failure);
        }

        var plan = body.Sequences[(int)value.Constant];
        if (plan.Operation != id || (uint)plan.Receiver >= (uint)body.Places.Count ||
            (body.IsReachable(id) && (body.GetInputState(id, plan.Receiver) & PlaceState.MustInit) == 0))
        {
            return Fail("Sequence metadata requires initialized receiver storage.", out failure);
        }

        var receiver = body.Places[plan.Receiver].Type;
        var syntaxReceiver = operation.Source switch
        {
            FromEndIndexKoto fromEnd => ElementAccess.ValueSource(fromEnd.Operand),
            BinaryKoto binary => ElementAccess.ValueSource(binary.Left),
            // A bare array Place iterates through its implicit Slice, whose temporary is sourced by the loop itself.
            ForKoto { SharedIterable: not null } loop => plan.Kind == SequenceOperation.Slice || loop.SharedIterable.Kind == BoundTypeKind.Semantics ? ElementAccess.ValueSource(loop.Iterable) : loop,
            ForKoto loop => ElementAccess.ValueSource(loop.Iterable),
            _ => null,
        };
        var receiverPlace = body.Places[plan.Receiver];
        var acquiredSource = syntaxReceiver is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow &&
            (ReferenceTypes.IsArray(receiverPlace.Type) || ReferenceTypes.IsDynamicArray(receiverPlace.Type) || ReferenceTypes.IsDictionary(receiverPlace.Type)) && ReferenceEquals(receiverPlace.Type, SignatureType(this, borrow.BoundType))
            ? ElementAccess.ValueSource(borrow.Left) : syntaxReceiver;
        // A shared iterable written as an explicit borrow (dictionary@ref) is reborrowed from the evaluated borrow itself.
        var receiverSource = ElementAccess.ValueSource(receiverPlace.Source);
        if (syntaxReceiver is null || plan.Projection < -1 ||
            (plan.Projection < 0 && !ReferenceEquals(receiverSource, acquiredSource) && !ReferenceEquals(receiverSource, syntaxReceiver) &&
                !(syntaxReceiver.BoundSymbol is { } symbol && body.SymbolPlaces.TryGetValue(symbol, out var local) && local == plan.Receiver)))
        {
            return Fail("Sequence receiver does not match its evaluated source.", out failure);
        }

        if (plan.Kind == SequenceOperation.FromEnd)
        {
            var result = ValueType(body, id);
            if (operation.Source is not FromEndIndexKoto || !ReferenceEquals(receiver, BoundType.ISize) ||
                !ReferenceEquals(result, SignatureType(this, operation.Source.BoundType)) ||
                result is null || !ReferenceEquals(result.Symbol, library.Index) ||
                value.Count != 0 || plan.Projection != -1 || plan.End != -1 || plan.Element != -1 ||
                (uint)plan.Index >= (uint)id || ValuePlace(body.Operations[plan.Index]) != plan.Receiver ||
                !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                (body.IsReachable(id) && !this.Dominates(plan.Index, id)) ||
                this.aggregateLayouts.Get(result) is not { Fields.Length: 2 } indexLayout ||
                !this.TryGetLocation(operation.Source, directory, constants, out var indexLocation))
            {
                return Fail("From-end Index construction requires its evaluated isize offset and designated layout.", out failure);
            }

            function.AddScalar(EmissionOpcode.Sequence, id, [new(EmissionOperandKind.SlotAddress, operation.Place), this.PhysicalOperand(body, plan.Index), new(EmissionOperandKind.Integer, indexLayout.Offset(1))], place: body.Operations.Count + id, location: indexLocation, op: "FromEnd", check: ArithmeticCheckKind.Argument);
            return true;
        }

        if (plan.Kind == SequenceOperation.Length && plan.Index != -1)
        {
            return Fail("Length metadata carries no element index.", out failure);
        }

        var address = new EmissionOperand(EmissionOperandKind.SlotAddress, plan.Receiver);
        var borrowedArray = ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDynamicArray(receiver) || ReferenceTypes.IsDictionary(receiver) || FormattingTypes.IsSliceBorrow(receiver);
        if (!borrowedArray && value.Count != 0)
        {
            return Fail("Owned sequence metadata must not carry a reference operand.", out failure);
        }

        if (borrowedArray)
        {
            if (plan.Projection != -1 || value.Count != 1 || (uint)Input(body, id, 0) >= (uint)id ||
                ValuePlace(body.Operations[Input(body, id, 0)]) != plan.Receiver ||
                !ReferenceEquals(ValueType(body, Input(body, id, 0)), receiver) ||
                (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
            {
                return Fail("Borrowed sequence requires a dominating reference value.", out failure);
            }

            address = this.PhysicalOperand(body, Input(body, id, 0));
            receiver = receiver.Components[0];
        }

        if (plan.Projection >= 0)
        {
            if ((uint)plan.Projection >= (uint)body.Projections.Count)
            {
                return Fail("Missing sequence receiver projection.", out failure);
            }

            var projection = body.Projections[plan.Projection];
            if (projection.Root != plan.Receiver || projection.Operation >= id || !body.HasComparisonLoan(id, projection.Loan) ||
                !ReferenceEquals(body.Operations[projection.Operation].Source, syntaxReceiver) ||
                (body.IsReachable(id) && !this.Dominates(projection.Operation, id)))
            {
                return Fail("Sequence receiver projection is not available.", out failure);
            }

            receiver = SignatureType(this, body.Operations[projection.Operation].Source.BoundType)!;
            address = new(EmissionOperandKind.ElementAddress, projection.Operation);
        }

        if (plan.Kind is SequenceOperation.ArrayIterator or SequenceOperation.ArrayMoveRead)
        {
            var initialization = this.arrayIterators[plan.Receiver];
            if (borrowedArray || receiver.Kind != BoundTypeKind.Array || plan.Projection != -1 || plan.End != -1 || plan.Element != -1 ||
                operation.Source is not ForKoto { SharedIterable: null, IsTupleBinding: false } || initialization < 0 ||
                !ReferenceEquals(body.Operations[initialization].Source, operation.Source) ||
                !this.TryGetArrayElement(receiver.Components[0], out var item) ||
                !this.TryGetLocation(operation.Source, directory, constants, out var iteratorLocation))
            {
                return Fail("Array iteration requires its acquired owning handle and element layout.", out failure);
            }

            this.arrayRuntimeUsed = true;
            if (plan.Kind == SequenceOperation.ArrayIterator)
            {
                if (initialization != id || plan.Index != -1 || !ReferenceEquals(ValueType(body, id), BoundType.Unit))
                {
                    return Fail("Array iterator initialization has an invalid result or cursor.", out failure);
                }

                function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, 0)], op: "ArrayIterator");
                return true;
            }

            if (!ReferenceEquals(ValueType(body, id), item.Type) || (uint)plan.Index >= (uint)id ||
                !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                (body.IsReachable(id) && (!this.Dominates(initialization, id) || !this.Dominates(plan.Index, id))))
            {
                return Fail("Array iteration must initialize its cursor before taking an element.", out failure);
            }

            var take = this.GetArrayHelper(ArrayHelperKind.Take, item).Abi;
            Span<EmissionOperand> arguments = stackalloc EmissionOperand[4];
            arguments[0] = address;
            var count = 1;
            if (!item.IsScalar)
            {
                arguments[count++] = new(EmissionOperandKind.SlotAddress, operation.Place);
            }

            arguments[count++] = new(EmissionOperandKind.ConstantAddress, iteratorLocation);
            arguments[count++] = new(EmissionOperandKind.ConstantLength, iteratorLocation);
            function.AddCall(id, take, arguments[..count]);
            return true;
        }

        if (receiver.Kind == BoundTypeKind.Dictionary && operation.Source is ForKoto)
        {
            return this.LowerDictionaryIteration(body, function, id, plan, receiver, borrowedArray, address, out failure);
        }

        if (plan.Kind == SequenceOperation.Borrow)
        {
            var reference = ValueType(body, id);
            if (!this.TrySequenceComponent(receiver, operation.Source, plan.Element, out var component, out var tupleLayout))
            {
                return Fail("Iteration component does not match its Tuple binding shape.", out failure);
            }

            var originSource = Binding.PlaceOriginSource(syntaxReceiver);
            var sameOrigin = receiver.Kind == BoundTypeKind.Slice || borrowedArray
                ? ReferenceEquals(reference?.Origin, receiverPlace.Type.Origin)
                : reference?.Origin is { Kind: OriginKind.Projection } origin && ReferenceEquals(origin.Binder, Binding.PlaceOriginBinder(originSource)) && origin.Slot == Binding.PlaceOriginSlot(originSource);
            // SPEC 14.6.2, 4.6.9: exclusive enumeration and an exclusive element borrow address the elements through a uniq
            // array reference.
            var exclusiveElements = borrowedArray && receiverPlace.Type.Semantics == SemanticsKind.Uniq &&
                (operation.Source is ForKoto { Mode: SubjectMode.Exclusive } || (operation.Source is IndexKoto && reference?.Semantics == SemanticsKind.Uniq));
            var fixedElements = receiver.Kind == BoundTypeKind.FixedArray && exclusiveElements;
            if ((receiver.Kind is not (BoundTypeKind.Slice or BoundTypeKind.Array) && !fixedElements) || !ReferenceTypes.IsStorage(reference) ||
                reference!.Semantics != (exclusiveElements ? SemanticsKind.Uniq : SemanticsKind.Ref) ||
                !ReferenceEquals(reference.Components[0], component) || !sameOrigin ||
                (uint)plan.Index >= (uint)id || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                (body.IsReachable(id) && !this.Dominates(plan.Index, id)) ||
                FunctionAbi.GetValue(receiver.Components[0], this.aggregateLayouts) is not { } element ||
                !this.TryGetLocation(operation.Source, directory, constants, out var borrowLocation))
            {
                return Fail("Sequence element borrow requires a checked index and matching backing Origin.", out failure);
            }

            if (fixedElements && (receiver.Length == 0 || this.aggregateLayouts.Get(receiver)?.Value.Layout.Size == 0))
            {
                address = new(EmissionOperandKind.NullAddress, 0);
            }

            var bound = new EmissionOperand(EmissionOperandKind.Integer, fixedElements ? receiver.Length : -1);
            ReadOnlySpan<EmissionOperand> borrowedOperands = tupleLayout is null
                ? [address, this.PhysicalOperand(body, plan.Index), bound]
                : [address, this.PhysicalOperand(body, plan.Index), bound,
                    new(EmissionOperandKind.Integer, tupleLayout.Value.Layout.Stride), new(EmissionOperandKind.Integer, tupleLayout.Offset(plan.Element))];
            function.AddScalar(EmissionOpcode.Sequence, id, borrowedOperands, place: body.Operations.Count + id, location: borrowLocation, op: fixedElements ? "ArrayAddress" : "SliceAddress", check: ArithmeticCheckKind.Bounds, representation: element);
            return true;
        }

        if (plan.Kind is SequenceOperation.Read or SequenceOperation.ArrayRead)
        {
            var arrayRead = plan.Kind == SequenceOperation.ArrayRead || (borrowedArray && receiver.Kind == BoundTypeKind.FixedArray);
            if (arrayRead && (receiver.Length == 0 || this.aggregateLayouts.Get(receiver)?.Value.Layout.Size == 0))
            {
                address = new(EmissionOperandKind.NullAddress, 0);
            }

            var validSource = borrowedArray ? operation.Source is IndexKoto index && (ReferenceTypes.IsArray(SignatureType(this, index.Left.BoundType)) || ReferenceTypes.IsDynamicArray(SignatureType(this, index.Left.BoundType))) :
                arrayRead ? operation.Source is ForKoto { Iterable.BoundType.Kind: BoundTypeKind.FixedArray } :
                operation.Source is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Slice or BoundTypeKind.Array } or ForKoto { IsTupleBinding: true };
            if (!this.TrySequenceComponent(receiver, operation.Source, plan.Element, out var readType, out var itemLayout))
            {
                return Fail("Iteration component does not match its Tuple binding shape.", out failure);
            }

            var aggregate = this.aggregateLayouts.Get(ValueType(body, id)!);
            if ((arrayRead ? receiver.Kind != BoundTypeKind.FixedArray : receiver.Kind is not (BoundTypeKind.Slice or BoundTypeKind.Array)) || !validSource ||
                (!ReferenceEquals(ValueType(body, id), readType) && !SharedReadTypes.ReadsStoredPointer(readType!, ValueType(body, id))) ||
                (!ReferenceTypes.IsValue(ValueType(body, id)) && aggregate is null && !ReferenceEquals(ValueType(body, id), BoundType.Unit)) ||
                body.Places[operation.Place].Acquisition != AcquisitionKind.Copy ||
                (uint)plan.Index >= (uint)id || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                (body.IsReachable(id) && !this.Dominates(plan.Index, id)) || !this.TryGetLocation(operation.Source, directory, constants, out var location))
            {
                return Fail("Slice read requires a protected handle and an isize index.", out failure);
            }

            ReadOnlySpan<EmissionOperand> operands = plan.Element < 0
                ? [address, this.PhysicalOperand(body, plan.Index), new(EmissionOperandKind.Integer, arrayRead ? receiver.Length : -1)]
                : [address, this.PhysicalOperand(body, plan.Index), new(EmissionOperandKind.Integer, arrayRead ? receiver.Length : -1),
                    new(EmissionOperandKind.Integer, itemLayout!.Value.Layout.Stride), new(EmissionOperandKind.Integer, itemLayout.Offset(plan.Element))];
            var storage = aggregate is not null || ReferenceEquals(ValueType(body, id), BoundType.Unit);
            function.AddScalar(EmissionOpcode.Sequence, id, operands, place: body.Operations.Count + id, location: location, op: storage ? arrayRead ? "ArrayStorageRead" : "SliceStorageRead" : arrayRead ? "ArrayRead" : "Read", check: ArithmeticCheckKind.Bounds, representation: aggregate?.Value ?? WindowsLowering.GetValue(ValueType(body, id)!));
            if (aggregate is { Value.Layout.Size: > 0 })
            {
                var start = function.Operands.Count;
                function.Operands.Add(new(EmissionOperandKind.ElementAddress, id));
                function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, operation.Place, OperandStart: start, OperandCount: 1, Aggregate: aggregate));
            }

            return true;
        }

        if (plan.Kind == SequenceOperation.Slice)
        {
            // A whole-range Slice is written values[..] or implied by bare iteration over an array Place (SPEC 14.6.2).
            Koto? startSyntax = null;
            Koto? endSyntax = null;
            BoundType? sliceType = null;
            if (operation.Source is IndexKoto { Right: RangeKoto { IsInclusive: false } rangeSyntax } source)
            {
                startSyntax = rangeSyntax.Start;
                endSyntax = rangeSyntax.End;
                sliceType = SignatureType(this, source.BoundType);
            }
            else if (operation.Source is ForKoto { SharedIterable: { } implicitSlice })
            {
                sliceType = SignatureType(this, implicitSlice);
            }

            if (receiver.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array) || sliceType is not { Kind: BoundTypeKind.Slice, Origin: not null } slice ||
                !ReferenceEquals(slice, ValueType(body, id)) || !ReferenceEquals(slice.Components[0], receiver.Components[0]))
            {
                return Fail("Slice construction requires a full sequence and its backing Origin.", out failure);
            }

            if (receiver.Kind == BoundTypeKind.FixedArray && this.aggregateLayouts.Get(receiver)!.Value.Layout.Size == 0)
            {
                address = new(EmissionOperandKind.NullAddress, 0);
            }

            if (!Endpoint(startSyntax, plan.Index) || !Endpoint(endSyntax, plan.End) ||
                FunctionAbi.GetValue(receiver.Components[0], this.aggregateLayouts) is not { } sliceElement ||
                !this.TryGetLocation(operation.Source, directory, constants, out var sliceLocation))
            {
                return Fail("Slice bounds must have matching evaluated isize endpoints.", out failure);
            }

            ReadOnlySpan<EmissionOperand> bounds = [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1),
                plan.Index < 0 ? new(EmissionOperandKind.Integer, 0) : this.PhysicalOperand(body, plan.Index),
                plan.End < 0 ? new(EmissionOperandKind.Integer, -1) : this.PhysicalOperand(body, plan.End), new(EmissionOperandKind.Integer, plan.End < 0 ? 1 : 0),
                new(EmissionOperandKind.Integer, body.Operations.Count + id)];
            function.AddScalar(EmissionOpcode.Sequence, id, bounds, place: operation.Place, location: sliceLocation, op: "SliceRange", check: ArithmeticCheckKind.Bounds, representation: sliceElement);
            return true;

            bool Endpoint(Koto? syntax, int producer) => syntax is null ? producer == -1 :
                (uint)producer < (uint)id && ReferenceEquals(body.Operations[producer].Source, syntax) &&
                ReferenceEquals(ValueType(body, producer), BoundType.ISize) && (!body.IsReachable(id) || this.Dominates(producer, id));
        }

        var name = plan.Kind switch
        {
            SequenceOperation.Indices => "indices",
            SequenceOperation.Start => "start",
            SequenceOperation.End => "end",
            SequenceOperation.IsEmpty => "isEmpty",
            SequenceOperation.Length => "length",
            SequenceOperation.Capacity => "capacity",
            _ => null,
        };
        if (name is null || (operation.Source is not ForKoto && operation.Source is not MemberAccessKoto { Right: IdentifierNameKoto }) ||
            (operation.Source is MemberAccessKoto { Right: IdentifierNameKoto member } && member.IdentifierName != name) ||
            (receiver.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.ResolvedRange or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary) &&
                !(FormattingTypes.IsUtf8Slice(receiver) && plan.Kind == SequenceOperation.Length && this.aggregateLayouts.Get(receiver) is { Value.Layout.Size: 16, Fields.Length: 1 } viewLayout && viewLayout.Offset(0) == 0)) ||
            (plan.Kind == SequenceOperation.Capacity && receiver.Kind is not (BoundTypeKind.Array or BoundTypeKind.Dictionary)) ||
            (plan.Kind == SequenceOperation.Indices ? operation.Source is not MemberAccessKoto { Right: IdentifierNameKoto { IdentifierName: "indices" } } ||
                receiver.Kind == BoundTypeKind.ResolvedRange || !ReferenceEquals(ValueType(body, id), BoundType.ResolvedRange) :
                plan.Kind == SequenceOperation.IsEmpty ? !ReferenceEquals(ValueType(body, id), BoundType.Boolean) : !ReferenceEquals(ValueType(body, id), BoundType.ISize)))
        {
            return Fail("Sequence operation does not match its source and result Type.", out failure);
        }

        function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1), new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.Dictionary ? 24 : 8)], place: operation.Place, op: name, representation: receiver.Kind == BoundTypeKind.ResolvedRange ? WindowsLowering.Unit : null);
        return true;
    }
}
