// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool[] materializedScalars = [];

    private static BoundArgumentOperation CallReceiverOperation(BoundCall call)
    {
        if (call.Receiver is null && call.Target.ReceiverIndex >= 0)
        {
            foreach (var argument in call.ArgumentOperations)
            {
                if (argument.ParameterIndex == call.Target.ReceiverIndex)
                {
                    return argument;
                }
            }
        }

        return call.Target.Declaration is FunctionKoto { Accessor.Receiver: not null } && call.ArgumentOperations.Length != 0
            ? call.ArgumentOperations[^1] : call.ReceiverOperation;
    }

    private static BoundArgumentOperation MemberReceiverOperation(MemberAccessKoto member, SemanticsKind semantics)
    {
        var call = member.Parent is InvocationKoto invocation && ReferenceEquals(invocation.Method, member) ? invocation.BoundCall
            : member.CodeContext.Compilation.Binding.PropertyCall(member, semantics == SemanticsKind.Ref ? PropertyAccessorKind.Get : PropertyAccessorKind.Set)?.BoundCall;
        return call is null ? default : CallReceiverOperation(call);
    }

    // The reference-typed base of a borrowed Field/Tuple path, or an Array element whose own borrow, of the element's complete stored
    // Type in the part's mode, is the single receiver (PLAN G59).
    private static Koto? ProjectedRoot(OwnershipBody body, int id, BinaryKoto projected, BoundType type, int inputs, out bool element)
    {
        element = false;
        if (projected is MemberAccessKoto member && ElementAccess.BorrowedPathRoot(member) is { } root)
        {
            return root;
        }

        if (inputs != 1 || ElementAccess.ElementPathBase(projected) is not { } elementBase ||
            type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ||
            !ReferenceEquals(type.Components[0], body.Resolve(elementBase.BoundType, body.ContextAt(id))) ||
            !ReferenceEquals(KotoHelper.UnwrapParentheses(body.Operations[Input(body, id, 0)].Source), elementBase))
        {
            return null;
        }

        element = true;
        return elementBase;
    }

    // A Scalar or Unit temporary, join result, by-value parameter or Subject receives its own slot only when it is borrowed
    // (SPEC 3.6.2, 10.2, 14.8.3); Unit needs only the address.
    // A temporary or join result is stored at its borrow from its one prepared value; a parameter is stored once at its
    // entry Produce, which dominates every borrow, so repeated and branch-local borrows share that slot.
    private void PrepareMaterializedScalars(OwnershipBody body)
    {
        Grow(ref this.materializedScalars, body.Places.Count);
        this.materializedScalars.AsSpan(0, body.Places.Count).Clear();
        if (body.DefaultInputs is { } inputs)
        {
            foreach (var input in inputs)
            {
                var type = body.Places[input.Place].Type;
                this.materializedScalars[input.Place] = IsScalar(type) || ReferenceTypes.IsString(type) || ReferenceEquals(type, BoundType.Unit);
            }
        }

        for (var i = 0; i < body.Operations.Count; i++)
        {
            if (body.Operations[i] is { Kind: OwnershipOperationKind.Borrow, Place: >= 0 } borrow && borrow.Place < body.Places.Count &&
                body.Places[borrow.Place] is { Kind: OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result or OwnershipPlaceKind.Parameter or OwnershipPlaceKind.Subject } place &&
                (ScalarTypes.Supports(place.Type) || ReferenceEquals(place.Type, BoundType.Unit)))
            {
                this.materializedScalars[borrow.Place] = true;
            }
        }
    }

    private bool IsMaterializedScalar(int place) => this.materializedScalars[place];

    private bool LowerStructBorrow(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (value.Kind == OwnershipValueKind.Address)
        {
            // All supported storage borrows use the same address and lifetime checks.
            if (operation.Kind != OwnershipOperationKind.Borrow || !(ReferenceTypes.IsBorrow(ValueType(body, id)) || ReferenceTypes.IsString(ValueType(body, id))) ||
                (uint)operation.Place >= (uint)body.Places.Count || value.Constant != operation.Place ||
                (body.IsReachable(id) && (body.GetBorrowInputState(id) & PlaceState.MustInit) == 0))
            {
                return Fail("Borrow address requires initialized storage and a verified reference result.", out failure);
            }

            var type = body.Places[operation.Place].Type;
            var output = ValueType(body, id)!;

            // SPEC 3.4, 5.4, 10.2 (PLAN G59 U1): a Place selection is borrowed through its own route. Borrowing the slot of a
            // temporary read from the same selection borrows a snapshot, admitted only as the counted shared borrow of a Copy part,
            // never for an exclusive or address-producing use, which would update or expose the copy. A Reborrow through a read
            // stored reference or handle addresses the referent and is not a snapshot.
            if (body.Places[operation.Place] is { Kind: OwnershipPlaceKind.Temporary, Source: BinaryKoto snapshot } snapshotPlace &&
                ReferenceEquals(KotoHelper.UnwrapParentheses(operation.Source), snapshot) && ElementAccess.IsSyntax(snapshot) && !ElementAccess.IsSlicing(snapshot) &&
                output.Components.Count == 1 && ReferenceTypes.StorageMatches(type, output.Components[0]) &&
                (output.Semantics is not (SemanticsKind.Ref or SemanticsKind.ObjRef) || operation.LoanMode != LoanRequirement.Ref ||
                    snapshotPlace.Acquisition != AcquisitionKind.Copy ||
                    (operation.Source.Parent is ConversionKoto addressConversion && ReferenceEquals(addressConversion.Left, operation.Source) &&
                        ElementAccess.ConversionKind(addressConversion, body, id) == ConversionBinding.Address)))
            {
                return Fail("A Place borrow requires its Place route, not a temporary read from the selection.", out failure);
            }

            if (type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef && output.Semantics == type.Semantics &&
                !ReferenceTypes.StorageMatches(type.Components[0], output.Components[0]) &&
                operation.Source is InvocationKoto { BoundCall: { } projectedPlan } &&
                CallReceiverOperation(projectedPlan) is { Kind: ArgumentOperationKind.BaseBorrow } projectedCall)
            {
                var sourceType = body.Resolve(projectedCall.SourceType, body.ContextAt(id));
                var sourceCore = ObjectTypes.IsBorrow(output) ? ObjectTypes.ViewTarget(sourceType) : ReferenceTypes.IsReference(sourceType) || ObjectTypes.HandleMode(sourceType) is not null || ObjectTypes.IsBorrow(sourceType) ? sourceType!.Components[0] : sourceType;
                if (projectedCall.ObjectCompatibility != ConstraintProof.Proven || projectedCall.BasePath is null ||
                    value.Count != 1 ||
                    !ReferenceTypes.StorageMatches(type.Components[0], sourceCore) ||
                    !ReferenceTypes.StorageMatches(output.Components[0], body.Resolve(projectedCall.BasePath.Type, body.ContextAt(id))) ||
                    !ReferenceTypes.StorageMatches(ValueType(body, Input(body, id, 0)), type) ||
                    (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
                {
                    return Fail("Base receiver borrow requires its proven projection and prepared source reference.", out failure);
                }

                // Inline bases share the payload prefix; object Views retain the original header address.
                function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0))]);
                return true;
            }

            if (operation.Projection >= 0)
            {
                if (!this.ValidateElementBorrow(body, id, id) || output.Semantics is not (SemanticsKind.Ref or SemanticsKind.Uniq) || value.Count != 0 ||
                    !ReferenceEquals(body.Resolve(operation.Source.BoundType, body.ContextAt(id)), output.Components[0]))
                {
                    return Fail("Stored element borrow requires its protected projection and complete stored Type.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.ElementAddress, body.Projections[operation.Projection].Operation)]);
                return true;
            }

            if (ObjectTypes.IsBorrow(output))
            {
                if (ReferenceTypes.IsStorage(type) && type.Components[0] is { } storedHandle && (ObjectTypes.HandleMode(storedHandle) is not null || ObjectTypes.IsBorrow(storedHandle)) &&
                    (output.Semantics == SemanticsKind.ObjRef || (type.Semantics == SemanticsKind.Uniq &&
                        (storedHandle.Semantics == SemanticsKind.ObjUniq || ObjectTypes.HandleMode(storedHandle) is { PayloadAuthority: LoanRequirement.Uniq }))) &&
                    value.Count == 1 && ReferenceEquals(storedHandle.Components[0], output.Components[0]) &&
                    ReferenceEquals(ValueType(body, Input(body, id, 0)), type) && (!body.IsReachable(id) || this.Dominates(Input(body, id, 0), id)))
                {
                    // SPEC 13.5.5.2: @objref of an object handle selected through a reference reads the handle at that address.
                    function.AddScalar(EmissionOpcode.ObjectBorrow, id, [this.PhysicalOperand(body, Input(body, id, 0))]);
                    return true;
                }

                var upcast = (ObjectTypes.HandleMode(type) is not null || ObjectTypes.IsBorrow(type)) &&
                    operation.Source.Parent is ConversionKoto conversion && ElementAccess.ConversionKind(conversion, body, id) == ConversionBinding.ObjectUpcast &&
                    ReferenceEquals(conversion.Left, operation.Source) && ReferenceEquals(body.Resolve(conversion.BoundType, body.ContextAt(id)), output) &&
                    ObjectTypes.Supports(type.Components[0], output.Components[0]);
                if (!(ObjectTypes.HandleMode(type) is not null || ObjectTypes.IsBorrow(type)) || (!ReferenceEquals(type.Components[0], output.Components[0]) && !upcast) ||
                    (output.Semantics == SemanticsKind.ObjUniq && type.Semantics != SemanticsKind.ObjUniq &&
                        ObjectTypes.HandleMode(type) is not { PayloadAuthority: LoanRequirement.Uniq }))
                {
                    return Fail("Object borrow requires matching view and exclusive authority.", out failure);
                }

                if (ObjectTypes.HandleMode(type) is not null && value.Count == 0)
                {
                    function.AddScalar(EmissionOpcode.ObjectBorrow, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
                    return true;
                }

                if (ObjectTypes.IsBorrow(type) && value.Count == 1 && ReferenceEquals(ValueType(body, Input(body, id, 0)), type) &&
                    (!body.IsReachable(id) || this.Dominates(Input(body, id, 0), id)))
                {
                    function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0))]);
                    return true;
                }

                return Fail("Object borrow requires its initialized owner or parent borrow.", out failure);
            }

            // A field projection borrows its slot, including through an object view; it is not a complete payload borrow. Below an
            // Array element, the receiver is that element's own borrow (PLAN G59).
            if (operation.Source is BinaryKoto projected && !ReceiverField(body, operation.Place) &&
                body.Resolve(projected.BoundType, body.ContextAt(id)) is var projectedType &&
                (!ReferenceTypes.IsStorage(projectedType) || ReferenceEquals(projectedType, output.Components[0])) &&
                ProjectedRoot(body, id, projected, type, value.Count, out var throughElement) is { } projectedRoot)
            {
                if (!this.TryBorrowedPathOffset(body, id, projected, projectedRoot, out var projectedOffset) || value.Count != 1 ||
                    !(throughElement || ElementAccess.ReceiverMatches(type, body.Resolve(ElementAccess.AccessType(projectedRoot, type.Semantics == SemanticsKind.Uniq), body.ContextAt(id)), projectedRoot)) ||
                    !ReferenceEquals(ValueType(body, Input(body, id, 0)), type) ||
                    !ReferenceEquals(projectedType, output.Components[0]) ||
                    (output.Semantics == SemanticsKind.Uniq && type.Semantics is not (SemanticsKind.Uniq or SemanticsKind.ObjUniq)) ||
                    (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
                {
                    return Fail("Projected borrow lacks a matching stored field and typed receiver.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0)), new(EmissionOperandKind.Integer, projectedOffset)]);
                return true;
            }

            if ((ObjectTypes.HandleMode(type) is not null || ObjectTypes.IsBorrow(type)) && !ReferenceTypes.StorageMatches(type, output.Components[0]))
            {
                var explicitProjection = operation.Source.Parent is ConversionKoto { ConversionBinding: ConversionBinding.PayloadFollow } selected &&
                    ReferenceEquals(selected.Left, operation.Source) && ReferenceEquals(body.Resolve(selected.BoundType, body.ContextAt(id)), output.Components[0]) &&
                    (output.Semantics == SemanticsKind.Ref ||
                        (selected.Parent is ConversionKoto conversion && ElementAccess.ConversionKind(conversion, body, id) == ConversionBinding.Borrow &&
                            ReferenceEquals(conversion.Left, selected) && ReferenceEquals(body.Resolve(conversion.BoundType, body.ContextAt(id)), output)));
                var memberProjection = operation.Source.Parent is MemberAccessKoto selectedMember &&
                    MemberReceiverOperation(selectedMember, output.Semantics) is { ObjectCompatibility: ConstraintProof.Proven } memberReceiver &&
                    ReferenceEquals(memberReceiver.Source, operation.Source) &&
                    ((memberReceiver.Kind == ArgumentOperationKind.PayloadProjection && ReferenceTypes.StorageMatches(body.Resolve(memberReceiver.ParameterType, body.ContextAt(id)), output)) ||
                    (memberReceiver.Kind == ArgumentOperationKind.BaseBorrow && output.Semantics == SemanticsKind.Ref && memberReceiver.BasePath is not null));
                if (!ReferenceEquals(type.Components[0], output.Components[0]) || !(explicitProjection || memberProjection) ||
                    (output.Semantics == SemanticsKind.Uniq && type.Semantics != SemanticsKind.ObjUniq &&
                        ObjectTypes.HandleMode(type) is not { PayloadAuthority: LoanRequirement.Uniq }))
                {
                    return Fail("Object payload address requires a proved complete-payload projection.", out failure);
                }

                if (ObjectTypes.HandleMode(type) is not null && value.Count == 0)
                {
                    function.AddScalar(EmissionOpcode.ObjectPayload, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
                    return true;
                }

                if (ObjectTypes.IsBorrow(type) && value.Count == 1 && ReferenceEquals(ValueType(body, Input(body, id, 0)), type) &&
                    (!body.IsReachable(id) || this.Dominates(Input(body, id, 0), id)))
                {
                    function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0)), new(EmissionOperandKind.Integer, 16)]);
                    return true;
                }

                return Fail("Payload projection lacks its prepared object reference.", out failure);
            }

            if (operation.Source is IndexKoto indexed && ReferenceTypes.IsArray(type))
            {
                // An element of a borrowed fixed array is borrowed in place through a bounds-checked
                // element address; a monomorphized instance sees its substituted element stride. The
                // array is a declared reference read here, or an element Place borrowed as the receiver.
                var element = type.Components[0].Components[0];
                var array = value.Count == 2 ? Input(body, id, 0) : -1;
                var subscript = value.Count == 2 ? Input(body, id, 1) : -1;
                var borrowedReceiver = array >= 0 && body.Operations[array].Kind == OwnershipOperationKind.Borrow;
                if ((uint)array >= (uint)id || (uint)subscript >= (uint)id || type.Semantics != SemanticsKind.Ref || operation.LoanMode != LoanRequirement.Ref ||
                    !(ReferenceTypes.IsStorage(output) || ReferenceTypes.IsString(output)) || output.Semantics != SemanticsKind.Ref || !ReferenceEquals(output.Components[0], element) ||
                    !ReferenceEquals(body.Resolve(ElementAccess.AccessType(indexed.Left), body.ContextAt(id)), borrowedReceiver ? type.Components[0] : type) || !ReferenceEquals(body.Resolve(indexed.BoundType, body.ContextAt(id)), element) ||
                    (!borrowedReceiver && body.Operations[array].Kind is not (OwnershipOperationKind.Read or OwnershipOperationKind.Produce)) ||
                    (borrowedReceiver ? body.Operations[array].Input : body.Operations[array].Place) != operation.Place ||
                    !ReferenceEquals(ValueType(body, array), type) || !ReferenceEquals(KotoHelper.UnwrapParentheses(body.Operations[array].Source), KotoHelper.UnwrapParentheses(indexed.Left)) ||
                    !ReferenceEquals(ValueType(body, subscript), BoundType.ISize) || !MatchesSelectionKeySource(body, subscript, indexed) ||
                    (body.IsReachable(id) && (!this.Dominates(array, id) || !this.Dominates(subscript, id))) ||
                    FunctionAbi.GetValue(element, this.aggregateLayouts) is not { } stride ||
                    !this.TryGetLocation(indexed, directory, constants, out var location))
                {
                    return Fail("Indexed borrow requires its borrowed array, an isize index and a concrete element stride.", out failure);
                }

                function.AddScalar(EmissionOpcode.Sequence, id, [this.PhysicalOperand(body, array), this.PhysicalOperand(body, subscript), new(EmissionOperandKind.Integer, type.Components[0].Length)], place: body.Operations.Count + id, location: location, op: "ArrayAddress", check: ArithmeticCheckKind.Bounds, representation: stride);
                return true;
            }

            if (ReferenceTypes.IsBorrow(type) && ReferenceTypes.StorageMatches(type, output.Components[0]))
            {
                // Borrow reference-value storage, not its referent. Locals already have a slot;
                // immutable by-value parameters/temporaries materialize their prepared pointer once.
                var input = value.Count == 1 ? Input(body, id, 0) : -1;
                if (input < 0 || !ReferenceTypes.StorageMatches(ValueType(body, input), type) ||
                    (body.IsReachable(id) && !this.Dominates(input, id)))
                {
                    return Fail("Reference-value borrow requires its prepared pointer.", out failure);
                }

                var slot = operation.Place;
                if (body.Places[slot].Kind != OwnershipPlaceKind.Local)
                {
                    if (output.Semantics != SemanticsKind.Ref)
                    {
                        return Fail("Only immutable shared inspection may materialize a reference parameter.", out failure);
                    }

                    slot = function.SlotAddresses.Count;
                    function.SlotAddresses.Add(new(EmissionOperandKind.SlotAddress, slot));
                    function.Slots.Add(new(slot, WindowsLowering.StringReference));
                    function.AddScalar(EmissionOpcode.StoreScalar, id, [this.PhysicalOperand(body, input)], "ptr", place: slot, representation: WindowsLowering.StringReference);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.SlotAddress, slot)]);
            }
            else if (ReferenceTypes.IsStorage(type))
            {
                if (value.Count != 1 || (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)) ||
                    !ReferenceTypes.StorageMatches(type.Components[0], output.Components[0]) || (output.Semantics == SemanticsKind.Uniq && type.Semantics != SemanticsKind.Uniq))
                {
                    return Fail("Reborrow has no matching reference source.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0))]);
            }
            else if (operation.Source is BinaryKoto path && !Binding.IsGetterResult(path) && !ReceiverField(body, operation.Place) && ElementAccess.OwnedPathRoot(path) is { } owner)
            {
                if (value.Count != 0 || this.aggregatePlaces[operation.Place] is null || !ReferenceEquals(body.Resolve(owner.BoundType, body.ContextAt(id)), type) ||
                    !ReferenceEquals(body.Resolve(path.BoundType, body.ContextAt(id)), output.Components[0]) || !this.TryBorrowedPathOffset(body, id, path, owner, out var pathOffset))
                {
                    return Fail("Owned path borrow does not match its stored layout and Types.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.Integer, pathOffset)]);
            }
            else if (body.Places[operation.Place].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Subject or OwnershipPlaceKind.Result && ScalarTypes.Supports(type))
            {
                var input = value.Count == 1 ? Input(body, id, 0) : -1;
                if (input < 0 || !ReferenceEquals(ValueType(body, input), type) || !ReferenceEquals(type, output.Components[0]) ||
                    (body.IsReachable(id) && !this.Dominates(input, id)))
                {
                    return Fail("Scalar temporary borrow requires its prepared value.", out failure);
                }

                if (operation.Source is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter } symbol } &&
                    body.Places[operation.Place].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && !this.IsPreparedArgument(body, id, symbol, operation.Place))
                {
                    return Fail("A prepared argument borrow must address its own acquired slot.", out failure);
                }

                // Materialize the prepared value in the temporary's, join result's or Subject's slot, then borrow that slot.
                var slot = WindowsLowering.GetValue(type)!;
                function.AddScalar(EmissionOpcode.StoreScalar, id, [this.PhysicalOperand(body, input)], slot.ComputationType, place: operation.Place, representation: slot);
                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
            }
            else
            {
                if (value.Count != 0 || !ReferenceTypes.StorageMatches(type, output.Components[0]) ||
                    (this.aggregatePlaces[operation.Place] is null && !ReferenceEquals(type, BoundType.Unit) && !this.IsStringStorage(body.Places[operation.Place]) &&
                        !(ScalarTypes.Supports(type) && (body.Places[operation.Place].Kind == OwnershipPlaceKind.Local || this.IsMaterializedScalar(operation.Place)))))
                {
                    return Fail("Borrow source has no matching aggregate storage.", out failure);
                }

                if (operation.Source is IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Parameter } symbol } &&
                    body.Places[operation.Place].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && !this.IsPreparedArgument(body, id, symbol, operation.Place))
                {
                    return Fail("A prepared argument borrow must address its own acquired slot.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
            }

            return true;
        }

        var field = value.Kind == OwnershipValueKind.BorrowedField ? operation.Source as MemberAccessKoto
            : operation.Source switch
            {
                BinaryKoto binary => binary.CodeContext.Compilation.Binding.PropertyUpdateStorage(binary.Left) ?? binary.CodeContext.Compilation.Binding.StorageProjection(KotoHelper.UnwrapParentheses(binary.Left)) ?? KotoHelper.UnwrapParentheses(binary.Left) as MemberAccessKoto,
                UnaryKoto unary => unary.CodeContext.Compilation.Binding.PropertyUpdateStorage(unary.Operand) ?? KotoHelper.UnwrapParentheses(unary.Operand) as MemberAccessKoto,
                _ => null,
            };
        var receiver = Input(body, id, 0);
        var root = field is null ? null : ElementAccess.BorrowedPathRoot(field);
        var fieldType = field is null ? null : body.Resolve(field.BoundType, body.ContextAt(id));
        // Copy aggregate fields use the common byte transfer, preserving the enum tag and payload
        // without interpreting padding as typed values.
        var handle = fieldType is not null ? this.aggregateLayouts.Get(fieldType) : null;
        if (field is null || root is null ||
            (!ElementAccess.ReceiverMatches(ValueType(body, receiver), body.Resolve(ElementAccess.AccessType(root, ValueType(body, receiver)?.Semantics == SemanticsKind.Uniq), body.ContextAt(id)), root) &&
                !(value.Kind == OwnershipValueKind.BorrowedField && this.PreparedBorrowMatches(body, id, receiver, root))) || !(ReferenceTypes.IsValue(fieldType) || ReferenceEquals(fieldType, BoundType.Unit) || handle is not null) ||
            (body.IsReachable(id) && !this.Dominates(receiver, id)))
        {
            return Fail("Borrowed field access requires a dominating typed receiver.", out failure);
        }

        if (!this.TryBorrowedPathOffset(body, id, field, root, out var offset))
        {
            return Fail("Borrowed field path does not match its stored layout and Types.", out failure);
        }

        var representation = handle?.Value ?? WindowsLowering.GetValue(fieldType!)!;
        function.AddScalar(EmissionOpcode.ElementAddress, id, [this.PhysicalOperand(body, receiver), new(EmissionOperandKind.Integer, offset)], representation: representation);
        if (value.Kind == OwnershipValueKind.BorrowedField)
        {
            if (operation.Kind != OwnershipOperationKind.Produce || body.Places[operation.Place].Acquisition != AcquisitionKind.Copy || !ReferenceEquals(ValueType(body, id), fieldType) ||
                (handle is not null && this.aggregatePlaces[operation.Place] is null))
            {
                return Fail("Borrowed field read has no matching result.", out failure);
            }

            if (representation.Layout.Size == 0)
            {
                return true;
            }

            if (handle is not null)
            {
                var start = function.Operands.Count;
                function.Operands.Add(new(EmissionOperandKind.ElementAddress, id));
                function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, operation.Place, OperandStart: start, OperandCount: 1, Aggregate: handle));
            }
            else
            {
                function.AddScalar(EmissionOpcode.LoadElement, id, [], representation.ComputationType, place: id, representation: representation);
            }
        }
        else
        {
            var input = Input(body, id, 1);
            if (handle is not null || operation.Kind != OwnershipOperationKind.WriteBorrowedField || ValueType(body, receiver)?.Semantics != SemanticsKind.Uniq ||
                !ReferenceEquals(ValueType(body, input), fieldType) || (body.IsReachable(id) && !this.Dominates(input, id)))
            {
                return Fail("Borrowed field write requires exclusive access and a matching secured value.", out failure);
            }

            function.AddScalar(EmissionOpcode.StoreElement, id, [this.PhysicalOperand(body, input)], representation.ComputationType, place: id, representation: representation);
        }

        return true;
    }

    private bool PreparedBorrowMatches(OwnershipBody body, int read, int receiver, Koto root)
    {
        var place = ValuePlace(body.Operations[receiver]);
        return KotoHelper.UnwrapParentheses(root).BoundSymbol is { } symbol &&
            this.IsPreparedArgument(body, read, symbol, place) &&
            body.Operations[this.elementNextCalls[read]].Source is InvocationKoto { BoundCall: not null } &&
            ReferenceTypes.StorageMatches(body.Resolve(root.BoundType, body.ContextAt(read)), ValueType(body, receiver));
    }

    // Inline parts are contiguous in their containing layout: sum each
    // validated level's stored offset from the borrowed base, under the field operation's default and instance context.
    private bool TryBorrowedPathOffset(OwnershipBody body, int operation, BinaryKoto field, Koto root, out int offset)
    {
        // Object views point at the allocation header; ordinary borrows point at payload storage.
        var rootType = body.Resolve(ElementAccess.AccessType(root), body.ContextAt(operation));
        offset = ObjectTypes.IsBorrow(rootType) || ObjectTypes.HandleMode(rootType) is not null ? 16 : 0;
        for (var level = field; ;)
        {
            var position = ElementAccess.PathSelector(level, out var owner, out var element);
            owner = body.Resolve(owner, body.ContextAt(operation));
            element = body.Resolve(element, body.ContextAt(operation));
            var layout = owner is null ? null : this.aggregateLayouts.Get(owner);
            var selected = body.Resolve(level.BoundType, body.ContextAt(operation));
            if (layout is null || (uint)position >= (uint)layout.StorageCount || element is null || selected is null ||
                !level.CodeContext.Compilation.Binding.FitsVerifiedTypeAt(element, selected, level))
            {
                return false;
            }

            offset = checked(offset + layout.StorageOffset(position));
            var receiver = KotoHelper.UnwrapParentheses(level.Left);
            if (ReferenceEquals(receiver, KotoHelper.UnwrapParentheses(root)) ||
                (ElementAccess.FollowedReference(receiver) is { } reference && ReferenceEquals(KotoHelper.UnwrapParentheses(reference), KotoHelper.UnwrapParentheses(root))))
            {
                return true;
            }

            if (receiver is not BinaryKoto parent)
            {
                return false;
            }

            level = parent;
        }
    }
}
