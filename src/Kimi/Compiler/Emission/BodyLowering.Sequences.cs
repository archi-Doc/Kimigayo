// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerSequence(KimiLibrary library, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        // Sequence values retain their checked receiver's authority; they do not introduce an independent Loan.
        if (value.Constant < 0 || value.Constant >= body.Sequences.Count || operation.Kind != OwnershipOperationKind.Produce || operation.LoanMode != LoanRequirement.None)
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
            FromEndIndexKoto { Parent: BinaryKoto selection } => ElementAccess.ValueSource(selection.Left, body, id),
            BinaryKoto binary => ElementAccess.ValueSource(binary.Left, body, id),
            ForKoto loop => ElementAccess.ValueSource(loop.Iterable, body, id),
            _ => null,
        };
        var receiverPlace = body.Places[plan.Receiver];
        if (syntaxReceiver is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PairFollow } followed &&
            !ReferenceEquals(ElementAccess.ValueSource(receiverPlace.Source, body, id), syntaxReceiver))
        {
            // SPEC 13.5.5: a followed collection is reached through its reference, or is the operand Place for an owner layer.
            syntaxReceiver = ElementAccess.ValueSource(followed.Left, body, id);
        }

        var acquiredSource = syntaxReceiver is ConversionKoto borrow && ElementAccess.ConversionKind(borrow, body, id) == ConversionBinding.Borrow &&
            (ReferenceTypes.IsArray(receiverPlace.Type) || ReferenceTypes.IsDynamicArray(receiverPlace.Type) || ReferenceTypes.IsDictionary(receiverPlace.Type)) && ReferenceEquals(receiverPlace.Type, body.Resolve(borrow.BoundType, body.ContextAt(id)))
            ? ElementAccess.ValueSource(borrow.Left, body, id) : syntaxReceiver;
        // A shared iterable written as an explicit borrow (dictionary@ref) is reborrowed from the evaluated borrow itself.
        var receiverSource = ElementAccess.ValueSource(receiverPlace.Source, body, id);
        if (syntaxReceiver is null || plan.Projection < -1 ||
            (plan.Projection < 0 && !ReferenceEquals(receiverSource, acquiredSource) && !ReferenceEquals(receiverSource, syntaxReceiver) &&
                !(syntaxReceiver.BoundSymbol is { } symbol && body.TrySymbolPlaceAt(symbol, id, out var local) && local == plan.Receiver)))
        {
            return Fail("Sequence receiver does not match its evaluated source.", out failure);
        }

        if (plan.Address < -1 || (plan.Address >= 0 && plan.Kind != SequenceOperation.Borrow))
        {
            return Fail("Only an element borrow can reuse a located address.", out failure);
        }

        if (plan.Kind == SequenceOperation.Length && plan.Index != -1)
        {
            return Fail("Length metadata carries no element index.", out failure);
        }

        var address = new EmissionOperand(EmissionOperandKind.SlotAddress, plan.Receiver);
        var borrowedArray = ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDynamicArray(receiver) || ReferenceTypes.IsDictionary(receiver) || FormattingTypes.IsSliceBorrow(receiver) || ReferenceTypes.IsSlice(receiver);
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

            receiver = body.Resolve(body.Operations[projection.Operation].Source.BoundType, body.ContextAt(projection.Operation))!;
            address = new(EmissionOperandKind.ElementAddress, projection.Operation);
        }

        if (plan.Kind == SequenceOperation.FromEnd)
        {
            // SPEC 4.6.9: a written `^x` element key is `length - x`, computed without a check of its own; the element
            // access's one bounds check rejects an offset outside [1, length].
            if (operation.Source is not FromEndIndexKoto || receiver.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array) ||
                !ReferenceEquals(ValueType(body, id), BoundType.ISize) || plan.End != -1 || (uint)plan.Index >= (uint)id ||
                !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) || (body.IsReachable(id) && !this.Dominates(plan.Index, id)))
            {
                return Fail("From-end position requires its evaluated isize offset and a sequence receiver.", out failure);
            }

            if (receiver.Kind == BoundTypeKind.FixedArray && receiver.Length >= 0 && this.PhysicalOperand(body, plan.Index) is { Kind: EmissionOperandKind.Integer } offset)
            {
                this.Fold(id, unchecked((long)(receiver.Length - offset.Value))); // SPEC 4.6.8: `^c` on a fixed array is `N - c`.
                return true;
            }

            function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1), new(EmissionOperandKind.Integer, 8), this.PhysicalOperand(body, plan.Index)], place: operation.Place, op: "fromEnd");
            return true;
        }

        if (plan.Kind == SequenceOperation.Borrow)
        {
            var reference = ValueType(body, id);
            var originSource = Binding.PlaceOriginSource(syntaxReceiver);
            var sameOrigin = receiver.Kind == BoundTypeKind.Slice || borrowedArray
                ? ReferenceEquals(reference?.Origin, receiverPlace.Type.Origin)
                : reference?.Origin is { Kind: OriginKind.Projection } origin && ReferenceEquals(origin.Binder, Binding.PlaceOriginBinder(originSource)) && origin.Slot == Binding.PlaceOriginSlot(originSource);
            // SPEC 4.6.9: an exclusive element borrow addresses the elements through a uniq array reference.
            var exclusiveElements = borrowedArray && receiverPlace.Type.Semantics == SemanticsKind.Uniq &&
                operation.Source is IndexKoto && reference?.Semantics == SemanticsKind.Uniq;
            var fixedElements = receiver.Kind == BoundTypeKind.FixedArray && borrowedArray;
            if ((receiver.Kind is not (BoundTypeKind.Slice or BoundTypeKind.Array) && !fixedElements) || !ReferenceTypes.IsStorage(reference) ||
                reference!.Semantics != (exclusiveElements ? SemanticsKind.Uniq : SemanticsKind.Ref) || receiver.Components.Count != 1 ||
                !ReferenceEquals(reference.Components[0], receiver.Components[0]) || !sameOrigin ||
                (uint)plan.Index >= (uint)id || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                (body.IsReachable(id) && !this.Dominates(plan.Index, id)) ||
                FunctionAbi.GetValue(receiver.Components[0], this.aggregateLayouts) is not { } element ||
                !this.TryGetLocation(operation.Source, directory, constants, out var borrowLocation))
            {
                return Fail("Sequence element borrow requires a checked index and matching backing Origin.", out failure);
            }

            if (plan.Address >= 0)
            {
                // The physical address survives inspection; the new exclusive access is checked against the original
                // receiver, not acquired from a shared capability. No index or bounds check is evaluated again.
                if (!exclusiveElements || (uint)plan.Address >= (uint)id ||
                    body.Values[plan.Address] is not { Kind: OwnershipValueKind.Sequence, Constant: var previous } ||
                    (uint)previous >= (uint)body.Sequences.Count || body.Sequences[(int)previous] is not { Kind: SequenceOperation.Borrow, Address: -1 } located ||
                    located.Operation != plan.Address || located.Receiver != plan.Receiver || located.Projection != plan.Projection || located.Index != plan.Index || located.End != plan.End ||
                    !ReferenceEquals(body.Operations[plan.Address].Source, operation.Source) ||
                    ValueType(body, plan.Address) is not { Semantics: SemanticsKind.Ref, Components: [var stored] } prior ||
                    !ReferenceEquals(stored, reference.Components[0]) || !ReferenceEquals(prior.Origin, reference.Origin) ||
                    (body.IsReachable(id) && !this.Dominates(plan.Address, id)))
                {
                    return Fail("Replacement requires the same dominating located element and exclusive receiver authority.", out failure);
                }

                this.physicalValues[id] = this.physicalValues[plan.Address];
                return true;
            }

            if (fixedElements && (receiver.Length == 0 || this.aggregateLayouts.Get(receiver)?.Value.Layout.Size == 0))
            {
                address = new(EmissionOperandKind.NullAddress, 0);
            }

            var bound = new EmissionOperand(EmissionOperandKind.Integer, fixedElements ? receiver.Length : -1);
            ReadOnlySpan<EmissionOperand> borrowedOperands = [address, this.PhysicalOperand(body, plan.Index), bound];
            function.AddScalar(EmissionOpcode.Sequence, id, borrowedOperands, place: body.Operations.Count + id, location: borrowLocation, op: fixedElements ? "ArrayAddress" : "SliceAddress", check: ArithmeticCheckKind.Bounds, representation: element);
            return true;
        }

        if (plan.Kind == SequenceOperation.Read)
        {
            var arrayRead = borrowedArray && receiver.Kind == BoundTypeKind.FixedArray;
            if (arrayRead && (receiver.Length == 0 || this.aggregateLayouts.Get(receiver)?.Value.Layout.Size == 0))
            {
                address = new(EmissionOperandKind.NullAddress, 0);
            }

            var validSource = borrowedArray ? operation.Source is IndexKoto index && (ReferenceTypes.IsArray(body.Resolve(ElementAccess.AccessType(index.Left), body.ContextAt(id))) || ReferenceTypes.IsDynamicArray(body.Resolve(ElementAccess.AccessType(index.Left), body.ContextAt(id)))) :
                operation.Source is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Slice or BoundTypeKind.Array };
            var aggregate = this.aggregateLayouts.Get(ValueType(body, id)!);
            if ((arrayRead ? receiver.Kind != BoundTypeKind.FixedArray : receiver.Kind is not (BoundTypeKind.Slice or BoundTypeKind.Array)) || !validSource ||
                receiver.Components is not [var readType] ||
                (!ReferenceEquals(ValueType(body, id), readType) && !SharedReadTypes.ReadsStoredPointer(readType, ValueType(body, id))) ||
                (!ReferenceTypes.IsValue(ValueType(body, id)) && aggregate is null && !ReferenceEquals(ValueType(body, id), BoundType.Unit)) ||
                body.Places[operation.Place].Acquisition != AcquisitionKind.Copy ||
                (uint)plan.Index >= (uint)id || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                (body.IsReachable(id) && !this.Dominates(plan.Index, id)) || !this.TryGetLocation(operation.Source, directory, constants, out var location))
            {
                return Fail("Slice read requires a protected handle and an isize index.", out failure);
            }

            ReadOnlySpan<EmissionOperand> operands = [address, this.PhysicalOperand(body, plan.Index), new(EmissionOperandKind.Integer, arrayRead ? receiver.Length : -1)];
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
            // SPEC 4.6.4: a range selection is written with range boundaries or applied through one ResolvedRange key.
            Koto? startSyntax = null;
            Koto? endSyntax = null;
            Koto? resolvedKey = null;
            BoundType? sliceType = null;
            var shape = 0L;
            if (operation.Source is IndexKoto keyed && ElementAccess.IsResolvedSlice(keyed))
            {
                // SPEC 4.6.4: one ResolvedRange value, written or resolved from a Range, supplies both boundaries.
                resolvedKey = ElementAccess.KeySyntax(keyed);
                sliceType = body.Resolve(keyed.BoundType, body.ContextAt(id));
            }
            else if (operation.Source is IndexKoto { Right: RangeKoto rangeSyntax } source)
            {
                // SPEC 4.6.4: a `^x` boundary evaluates x and resolves against the length in the slice operation.
                startSyntax = rangeSyntax.Start is FromEndIndexKoto { Operand: var startOffset } ? startOffset : rangeSyntax.Start;
                endSyntax = rangeSyntax.End is FromEndIndexKoto { Operand: var endOffset } ? endOffset : rangeSyntax.End;
                sliceType = body.Resolve(source.BoundType, body.ContextAt(id));
                shape = (rangeSyntax.Start is FromEndIndexKoto ? SliceShape.StartFromEnd : 0) | (rangeSyntax.End is FromEndIndexKoto ? SliceShape.EndFromEnd : 0) | (rangeSyntax.IsInclusive ? SliceShape.Closed : 0);
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

            if (resolvedKey is not null)
            {
                if (plan.End != -1 || (uint)plan.Index >= (uint)id || !ReferenceEquals(body.Operations[plan.Index].Source, ElementAccess.ValueSource(resolvedKey, body, plan.Index)) ||
                    !ReferenceTypes.IsResolvedRange(ValueType(body, plan.Index)) || (body.IsReachable(id) && !this.Dominates(plan.Index, id)) ||
                    FunctionAbi.GetValue(receiver.Components[0], this.aggregateLayouts) is not { } resolvedElement ||
                    !this.TryGetLocation(operation.Source, directory, constants, out var resolvedLocation))
                {
                    return Fail("Slice application requires its evaluated ResolvedRange key.", out failure);
                }

                ReadOnlySpan<EmissionOperand> resolvedBounds = [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1),
                    new(EmissionOperandKind.SlotAddress, ValuePlace(body.Operations[plan.Index])), new(EmissionOperandKind.Integer, 0), new(EmissionOperandKind.Integer, 0),
                    new(EmissionOperandKind.Integer, body.Operations.Count + id)];
                function.AddScalar(EmissionOpcode.Sequence, id, resolvedBounds, place: operation.Place, location: resolvedLocation, op: "SliceResolved", check: ArithmeticCheckKind.Bounds, representation: resolvedElement);
                return true;
            }

            if (!Endpoint(startSyntax, plan.Index) || !Endpoint(endSyntax, plan.End) ||
                FunctionAbi.GetValue(receiver.Components[0], this.aggregateLayouts) is not { } sliceElement ||
                !this.TryGetLocation(operation.Source, directory, constants, out var sliceLocation))
            {
                return Fail("Slice bounds must have matching evaluated isize endpoints.", out failure);
            }

            ReadOnlySpan<EmissionOperand> bounds = [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1),
                plan.Index < 0 ? new(EmissionOperandKind.Integer, 0) : this.PhysicalOperand(body, plan.Index),
                plan.End < 0 ? new(EmissionOperandKind.Integer, -1) : this.PhysicalOperand(body, plan.End), new(EmissionOperandKind.Integer, shape | (plan.End < 0 ? SliceShape.EndOmitted : 0)),
                new(EmissionOperandKind.Integer, body.Operations.Count + id)];
            function.AddScalar(EmissionOpcode.Sequence, id, bounds, place: operation.Place, location: sliceLocation, op: "SliceRange", check: ArithmeticCheckKind.Bounds, representation: sliceElement);
            return true;

            // An isize boundary is used as evaluated, so its producer is the boundary's value source (parentheses, labels and
            // identity conversions removed); a boundary of another integer Type is converted at the written syntax.
            bool Endpoint(Koto? syntax, int producer) => syntax is null ? producer == -1 :
                (uint)producer < (uint)id && (ReferenceEquals(body.Operations[producer].Source, syntax) || ReferenceEquals(body.Operations[producer].Source, ElementAccess.ValueSource(syntax, body, producer))) &&
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
            (!ReferenceTypes.IsResolvedRange(receiver) && receiver.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary) &&
                !(FormattingTypes.IsUtf8Slice(receiver) && plan.Kind == SequenceOperation.Length && this.aggregateLayouts.Get(receiver) is { Value.Layout.Size: 16, Fields.Length: 1 } viewLayout && viewLayout.Offset(0) == 0)) ||
            (plan.Kind == SequenceOperation.Capacity && receiver.Kind is not (BoundTypeKind.Array or BoundTypeKind.Dictionary)) ||
            (plan.Kind == SequenceOperation.Indices ? operation.Source is not MemberAccessKoto { Right: IdentifierNameKoto { IdentifierName: "indices" } } ||
                ReferenceTypes.IsResolvedRange(receiver) || !ReferenceTypes.IsResolvedRange(ValueType(body, id)) :
                plan.Kind == SequenceOperation.IsEmpty ? !ReferenceEquals(ValueType(body, id), BoundType.Boolean) : !ReferenceEquals(ValueType(body, id), BoundType.ISize)))
        {
            return Fail("Sequence operation does not match its source and result Type.", out failure);
        }

        function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1), new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.Dictionary ? 24 : 8)], place: operation.Place, op: name, representation: ReferenceTypes.IsResolvedRange(receiver) ? WindowsLowering.Unit : null);
        return true;
    }
}
