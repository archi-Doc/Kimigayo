// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>SPEC 4.7.2, 4.7.4, 4.7.6: the Array mutation operations lower to runtime capacity routines and per-element helpers over the {buffer, length, capacity} handle.</summary>
internal sealed partial class BodyLowering
{
    private readonly Dictionary<(ArrayHelperKind Kind, int Layout, string Scalar, int Remainder, int Option), ArrayHelper> arrayHelpers = new();
    private readonly Dictionary<(ArrayHelperKind Kind, int Layout, string Scalar, int Remainder, int Option), ArrayHelper> arrayHelperCache = new();
    private bool arrayRuntimeUsed;
    private int[] arrayIterators = [];
    private int[] arrayIterationPlaces = [];

    private bool PrepareArrayIterators(OwnershipBody body, out string? failure)
    {
        failure = null;
        Grow(ref this.arrayIterators, body.Places.Count);
        this.arrayIterators.AsSpan(0, body.Places.Count).Fill(-1);
        Grow(ref this.arrayIterationPlaces, body.Places.Count);
        this.arrayIterationPlaces.AsSpan(0, body.Places.Count).Clear();
        foreach (var sequence in body.Sequences)
        {
            if (sequence.Kind is SequenceOperation.ArrayMoveRead or SequenceOperation.DictionaryMoveRead && (uint)sequence.Operation < (uint)body.Operations.Count &&
                body.Operations[sequence.Operation] is { Kind: OwnershipOperationKind.Produce, Source: ForKoto loop } produce &&
                (uint)produce.Place < (uint)body.Places.Count)
            {
                this.arrayIterationPlaces[produce.Place] = 1;
                var slot = sequence.Element < 0 ? 0 : sequence.Element;
                if ((uint)slot < (uint)loop.Bindings.Count && loop.Bindings[slot].BoundSymbol is { } symbol && body.SymbolPlaces.TryGetValue(symbol, out var binding))
                {
                    this.arrayIterationPlaces[binding] = 1;
                }
            }

            if (sequence.Kind != SequenceOperation.ArrayIterator)
            {
                continue;
            }

            if ((uint)sequence.Receiver >= (uint)body.Places.Count || (uint)sequence.Operation >= (uint)body.Operations.Count ||
                this.arrayIterators[sequence.Receiver] >= 0 || body.Places[sequence.Receiver] is not { Kind: OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result, Type.Kind: BoundTypeKind.Array } ||
                body.Operations[sequence.Operation].Source is not ForKoto { SharedIterable: null, IsTupleBinding: false })
            {
                return Fail("An owning Array iterator requires a private acquired handle.", out failure);
            }

            this.arrayIterators[sequence.Receiver] = sequence.Operation;
        }

        return true;
    }

    private readonly record struct ArrayElement(BoundType Type, ValueLowering Value, AggregateLayout? Layout, bool IsString)
    {
        internal bool IsScalar => this.Layout is null && !this.IsString && !ReferenceEquals(this.Type, BoundType.Unit);

        internal bool NeedsDestruction => this.IsString || this.Layout?.NeedsDestruction == true;

        internal long Stride => this.Value.Layout.Stride;
    }

    // SPEC 4.3: an Array literal's scalar payloads are stored in their own slots so construction can move their bytes into the buffer.
    private bool IsArrayPayload(OwnershipBody body, int place)
        => body.Places[place].Kind == OwnershipPlaceKind.Payload && this.payloadOwners[place] >= 0 &&
            (body.Places[body.Constructions[this.payloadOwners[place]].Place].Type.Kind == BoundTypeKind.Array ||
             body.Places[body.Constructions[this.payloadOwners[place]].Place].Source is ArrayLiteralKoto { FillLength: not null });

    // SPEC 4.5, 16.3.2: the drop helper that destroys an Array field's elements and releases its buffer with the containing struct.
    private string? ArrayFieldDrop(BoundType array)
    {
        return array.Kind == BoundTypeKind.Array && array.Components.Count == 1 && this.TryGetArrayElement(array.Components[0], out var element)
            ? this.GetArrayHelper(ArrayHelperKind.Drop, element).Abi.Name : null;
    }

    private bool TryGetArrayElement(BoundType type, out ArrayElement element, bool allowEmpty = false)
    {
        element = default;
        if (allowEmpty && ReferenceEquals(type, BoundType.Unit))
        {
            element = new(type, WindowsLowering.Unit, null, false);
            return true;
        }

        if (ReferenceEquals(type, BoundType.String))
        {
            element = new(type, WindowsLowering.String, null, true);
            return true;
        }

        if (IsScalar(type))
        {
            if (WindowsLowering.GetValue(type) is not { } scalar)
            {
                return false;
            }

            element = new(type, scalar, null, false);
            return true;
        }

        // Nested handles and zero-sized elements wait for their own storage plans (PLAN P29).
        if (type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.Never) ||
            this.aggregateLayouts.Get(type) is not { } layout || (!allowEmpty && layout.Value.Layout.Stride <= 0))
        {
            return false;
        }

        element = new(type, layout.Value, layout, false);
        return true;
    }

    private ArrayHelper GetArrayHelper(ArrayHelperKind kind, in ArrayElement element, AggregateLayout? option = null, AggregateLayout? remainder = null)
    {
        // A boundary helper also depends on its result and remainder records, so their layout ids join the key and name; the
        // key holds only ids and constant spellings, so a warm lookup allocates nothing.
        var key = (kind, element.Layout?.Id ?? -1, element.IsString ? "string" : element.IsScalar ? element.Value.ComputationType : string.Empty,
            remainder?.Id ?? -1, remainder is null ? -1 : option?.Id ?? -1);
        if (this.arrayHelpers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        // Keep syntax-free physical helpers across warm emission, but register only this module's used helpers.
        if (this.arrayHelperCache.TryGetValue(key, out existing) && ReferenceEquals(existing.Option, option) && ReferenceEquals(existing.Remainder, remainder))
        {
            this.arrayHelpers.Add(key, existing);
            return existing;
        }

        var suffix = element.Layout is { } layout ? "a" + layout.Id.ToString(CultureInfo.InvariantCulture) : element.IsString ? "string" : element.Value.ComputationType;
        var prefix = kind switch
        {
            ArrayHelperKind.Append => "__kimi_array_append_",
            ArrayHelperKind.Insert => "__kimi_array_insert_",
            ArrayHelperKind.InsertIndex => "__kimi_array_insert_index_",
            ArrayHelperKind.Pop => "__kimi_array_pop_",
            ArrayHelperKind.Remove => "__kimi_array_remove_",
            ArrayHelperKind.RemoveIndex => "__kimi_array_remove_index_",
            ArrayHelperKind.Place => "__kimi_array_place_",
            ArrayHelperKind.Clear => "__kimi_array_clear_",
            ArrayHelperKind.Take => "__kimi_array_take_",
            ArrayHelperKind.IteratorDrop => "__kimi_array_iterator_drop_",
            ArrayHelperKind.Swap => "__kimi_array_swap_",
            ArrayHelperKind.BorrowStorage => "__kimi_array_borrow_",
            ArrayHelperKind.OwnStorage => "__kimi_array_own_",
            _ => "__kimi_array_drop_",
        };
        var records = remainder is null ? string.Empty : "_r" + remainder.Id.ToString(CultureInfo.InvariantCulture) + (option is null ? string.Empty : "_o" + option.Id.ToString(CultureInfo.InvariantCulture));
        var name = prefix + suffix + records;
        var valueType = element.IsScalar ? element.Value.ComputationType : "ptr";
        var handle = new AbiParameter("ptr", "handle");
        var location = new AbiParameter("ptr", "location", AbiParameterKind.Location);
        var length = new AbiParameter("i64", "location_length", AbiParameterKind.LocationLength);
        var unit = WindowsLowering.Unit.ComputationType;
        var indexParameter = kind is ArrayHelperKind.InsertIndex or ArrayHelperKind.RemoveIndex ? new AbiParameter("ptr", "index_value") : new("i64", "index");
        FunctionAbi abi = kind switch
        {
            ArrayHelperKind.Append => new(name, unit, [handle, new(valueType, "value"), location, length]),
            ArrayHelperKind.Insert or ArrayHelperKind.InsertIndex => new(name, unit, [handle, indexParameter, new(valueType, "value"), location, length]),
            ArrayHelperKind.Pop => new(name, unit, [handle, new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            ArrayHelperKind.Place => new(name, unit, [handle, new("ptr", "source"), location, length]),
            ArrayHelperKind.Take => element.IsScalar
                ? new(name, element.Value.ComputationType, [handle, location, length])
                : new(name, unit, [handle, new("ptr", "result", AbiParameterKind.ResultSlot), location, length], resultSlot: true),
            ArrayHelperKind.Remove or ArrayHelperKind.RemoveIndex => element.IsScalar
                ? new(name, element.Value.ComputationType, [handle, indexParameter, location, length])
                : new(name, unit, [handle, indexParameter, new("ptr", "result", AbiParameterKind.ResultSlot), location, length], resultSlot: true),
            ArrayHelperKind.Swap => new(name, unit, [handle, new("i64", "first"), new("i64", "second"), location, length]),
            ArrayHelperKind.BorrowStorage => new(name, unit, [handle, new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            ArrayHelperKind.OwnStorage => new(name, unit, [new("ptr", "value"), new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            _ => new(name, unit, [handle, location, length]),
        };
        var helper = new ArrayHelper(kind, abi, element.Value, element.Layout, element.IsString, option, remainder);
        this.arrayHelperCache[key] = helper;
        this.arrayHelpers.Add(key, helper);
        return helper;
    }

    private bool LowerArrayOperation(KimiLibrary library, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var kind = plan.Target.CompilerFunction;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is null || call.AttributeChain is not null ||
            plan.ReceiverOperation.Kind is not (ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != call.ArgumentNodes.Count || plan.ArgumentToParameter.Length != call.ArgumentNodes.Count ||
            call.ArgumentNodes.Count + 1 != target.Parameters.Count || target.BoundSymbol?.ReceiverIndex != 0 ||
            SignatureType(this, plan.ReceiverOperation.ParameterType) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq } receiverType ||
            receiverType.Components[0] is not { Kind: BoundTypeKind.Array } arrayType || !this.TryGetArrayElement(arrayType.Components[0], out var element))
        {
            return Fail("Array operation has an unsupported receiver, argument plan or element Type.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, receiverType, out var handle))
        {
            return Fail("Array receiver borrow is unavailable at the call.", out failure);
        }

        var location = -1;
        if (kind != CompilerFunctionKind.ArrayPop && !this.TryGetLocation(call, directory, constants, out location))
        {
            return Fail("An Array operation has no diagnostic source location.", out failure);
        }

        var returnType = SignatureType(this, plan.ReturnType);
        if (!ReferenceEquals(SignatureType(this, call.BoundType), returnType) || returnType is null)
        {
            return Fail("Array operation result Type does not match its call.", out failure);
        }

        this.callOperands.Clear();
        this.callOperands.Add(handle);
        this.arrayRuntimeUsed = true;
        FunctionAbi callee;
        switch (kind)
        {
            case CompilerFunctionKind.ArrayReserve:
            case CompilerFunctionKind.ArrayShrinkToFit:
                callee = kind == CompilerFunctionKind.ArrayReserve ? WindowsLowering.ArrayReserve : WindowsLowering.ArrayShrink;
                this.callOperands.Add(new(EmissionOperandKind.Integer, element.Stride));
                if (kind == CompilerFunctionKind.ArrayReserve)
                {
                    if (!this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var additional))
                    {
                        return Fail("Array reserve amount is unavailable at the call.", out failure);
                    }

                    this.callOperands.Add(additional);
                }

                break;
            case CompilerFunctionKind.ArrayAppend:
            case CompilerFunctionKind.ArrayInsert:
            case CompilerFunctionKind.ArrayInsertIndex:
                var insert = kind != CompilerFunctionKind.ArrayAppend;
                callee = this.GetArrayHelper(kind == CompilerFunctionKind.ArrayInsertIndex ? ArrayHelperKind.InsertIndex : insert ? ArrayHelperKind.Insert : ArrayHelperKind.Append, element).Abi;
                if (insert)
                {
                    if (!this.ArrayIndexArgument(library, body, id, kind == CompilerFunctionKind.ArrayInsertIndex, out var index))
                    {
                        return Fail("Array insert index is unavailable at the call.", out failure);
                    }

                    this.callOperands.Add(index);
                }

                if (!this.ArrayValueArgument(body, id, insert ? 2 : 1, element, out var value))
                {
                    return Fail("Array element argument is not an initialized acquired value.", out failure);
                }

                this.callOperands.Add(value);
                break;
            case CompilerFunctionKind.ArrayPop:
                if (this.aggregateLayouts.Get(returnType) is not { Cases.Length: 2 } option || !SlotTypes.IsResult(returnType) || !this.ValidateSlotCallResult(body, id, out failure))
                {
                    return Fail(failure ?? "Array pop result is not a stored Option.", out failure);
                }

                callee = this.GetArrayHelper(ArrayHelperKind.Pop, element, option).Abi;
                this.callOperands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
                break;
            case CompilerFunctionKind.ArrayRemove:
            case CompilerFunctionKind.ArrayRemoveIndex:
                callee = this.GetArrayHelper(kind == CompilerFunctionKind.ArrayRemoveIndex ? ArrayHelperKind.RemoveIndex : ArrayHelperKind.Remove, element).Abi;
                if (!this.ArrayIndexArgument(library, body, id, kind == CompilerFunctionKind.ArrayRemoveIndex, out var removed))
                {
                    return Fail("Array remove index is unavailable at the call.", out failure);
                }

                this.callOperands.Add(removed);
                if (element.IsScalar)
                {
                    if (body.Values[id].Kind != OwnershipValueKind.Call || !ReferenceEquals(ValueType(body, id), element.Type) || !ReferenceEquals(returnType, element.Type))
                    {
                        return Fail("Array remove result does not match its element Type.", out failure);
                    }
                }
                else
                {
                    if (!ReferenceTypes.StorageMatches(element.Type, returnType) || !SlotTypes.IsResult(returnType) || !this.ValidateSlotCallResult(body, id, out failure))
                    {
                        return Fail(failure ?? "Array remove result is not a stored element.", out failure);
                    }

                    this.callOperands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
                }

                break;
            case CompilerFunctionKind.ArraySwap:
                // SPEC 4.7.2: both indices are checked, then the two element slots exchange their bytes; no element is
                // Copied as a value and no destructor runs.
                callee = this.GetArrayHelper(ArrayHelperKind.Swap, element).Abi;
                if (!this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var first) || !this.ScalarArrayArgument(body, id, 2, BoundType.ISize, out var second))
                {
                    return Fail("Array swap indices are unavailable at the call.", out failure);
                }

                this.callOperands.Add(first);
                this.callOperands.Add(second);
                break;
            default:
                callee = this.GetArrayHelper(ArrayHelperKind.Clear, element).Abi;
                break;
        }

        if (location >= 0)
        {
            this.callOperands.Add(new(EmissionOperandKind.ConstantAddress, location));
            this.callOperands.Add(new(EmissionOperandKind.ConstantLength, location));
        }

        if (this.callOperands.Count != callee.Parameters.Length)
        {
            return Fail("Array operation operands do not match the helper ABI.", out failure);
        }

        function.AddCall(id, callee, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }

    // SPEC 22.1.2.5: borrowStorage and ownStorage transfer the Array handle to a remainder; lend and split
    // publish one pointer-backed capability. The source functions advance the untaken range.
    private bool LowerStorageOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var kind = plan.Target.CompilerFunction;
        var owning = kind == CompilerFunctionKind.StorageOwn;
        if (kind == CompilerFunctionKind.StorageRelease)
        {
            return this.LowerStorageRelease(body, function, constants, directory, id, call, plan, out failure);
        }

        if (kind is CompilerFunctionKind.StorageBorrowDictionary or CompilerFunctionKind.StorageLendKey or CompilerFunctionKind.StorageLendValue)
        {
            return this.LowerDictionaryStorageOperation(body, function, id, call, plan, out failure);
        }

        if (kind is CompilerFunctionKind.StorageLend or CompilerFunctionKind.StorageSplit)
        {
            return this.LowerStorageCapability(body, function, id, call, plan, out failure);
        }

        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || plan.ArgumentToParameter.Length != 1 || target.Parameters.Count != 1)
        {
            return Fail("Storage operation has an unsupported argument plan.", out failure);
        }

        // A reference parameter is Copied, Reborrowed or borrowed from its Place, and every form supplies the pointer;
        // ownStorage takes the Array's acquired slot.
        var argumentType = SignatureType(this, plan.ArgumentOperations[0].ParameterType);
        var referent = kind == CompilerFunctionKind.StorageOwn ? argumentType : argumentType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var target0] } ? target0 : null;
        if (referent is null || argumentType is null || plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
            (kind == CompilerFunctionKind.StorageOwn && plan.ArgumentOperations[0].Kind != ArgumentOperationKind.Value))
        {
            return Fail("Storage operation has an unsupported argument acquisition.", out failure);
        }

        if (referent.Components is not [var elementType] || referent.Kind != BoundTypeKind.Array ||
            !this.TryGetArrayElement(elementType, out var element))
        {
            return Fail("Storage operation has an unsupported collection or element Type.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        EmissionOperand pointer;
        if (kind == CompilerFunctionKind.StorageOwn)
        {
            // The Array's acquired slot is transferred into the remainder; the call consumes it (SPEC 22.1.2.5).
            var entry = this.parameterArguments[0];
            var place = entry < 0 ? -1 : body.Operations[entry].Place;
            if (place < 0 || !ReferenceTypes.StorageMatches(argumentType, body.Places[place].Type) || !this.IsSlotValue(body.Places[place]) ||
                (body.IsReachable(entry) && (body.GetInputState(entry, place) & PlaceState.MustInit) == 0))
            {
                return Fail("Storage operation argument is not an initialized acquired Array.", out failure);
            }

            pointer = new(EmissionOperandKind.SlotAddress, place);
        }
        else if (!this.ScalarArrayArgument(body, id, 0, argumentType, out pointer))
        {
            return Fail("Storage operation argument borrow is unavailable at the call.", out failure);
        }

        var returnType = SignatureType(this, plan.ReturnType);
        if (returnType is null || !ReferenceEquals(SignatureType(this, call.BoundType), returnType) || this.aggregateLayouts.Get(returnType) is not { } result ||
            !SlotTypes.IsResult(returnType) || !this.ValidateSlotCallResult(body, id, out failure))
        {
            return Fail(failure ?? "Storage operation result is not a stored record.", out failure);
        }

        // The owning helpers and the remainder's drop share the record shape {storage, position, count, capacity}.
        var remainder = result;
        if (remainder is not { IsArray: false } || remainder.Fields.Length != (owning ? 4 : 3) ||
            (owning && (remainder.Offset(0) != 0 || remainder.Offset(1) != 8 || remainder.Offset(2) != 16 || remainder.Offset(3) != 24)))
        {
            return Fail("Storage operation records do not have the boundary's shape.", out failure);
        }

        this.arrayRuntimeUsed |= owning;
        var helperKind = kind switch
        {
            CompilerFunctionKind.StorageOwn => ArrayHelperKind.OwnStorage,
            _ => ArrayHelperKind.BorrowStorage,
        };
        var helper = this.GetArrayHelper(helperKind, element, remainder: remainder);
        this.callOperands.Clear();
        this.callOperands.Add(pointer);
        this.callOperands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
        function.AddCall(id, helper.Abi, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }

    // The validated, internal unsafe primitives publish the remainder's source Origin; their runtime value is the
    // supplied element pointer. Storage.kimi checks the untaken range and advances it before making this call.
    private bool LowerStorageCapability(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var exclusive = plan.Target.CompilerFunction == CompilerFunctionKind.StorageSplit;
        var semantics = exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref;
        var remainderId = exclusive ? KimiDeclarationId.UniqRemainder : KimiDeclarationId.RefRemainder;
        var stateArgument = plan.ArgumentToParameter.Length == 2 && plan.ArgumentToParameter[0] == 1 ? 1 : 0;
        var pointerArgument = 1 - stateArgument;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 2 || call.ArgumentNodes.Count != 2 || plan.ArgumentToParameter.Length != 2 || target.Parameters.Count != 2 ||
            plan.ArgumentToParameter[stateArgument] != 0 || plan.ArgumentToParameter[pointerArgument] != 1 ||
            SignatureType(this, plan.ArgumentOperations[stateArgument].ParameterType) is not { Kind: BoundTypeKind.Semantics, Components: [var remainder] } state || state.Semantics != semantics ||
            remainder.Symbol?.LibraryDeclaration != remainderId || remainder.Components is not [var element] ||
            SignatureType(this, plan.ArgumentOperations[pointerArgument].ParameterType) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components: [var pointee] } pointer || !ReferenceEquals(pointee, element) ||
            SignatureType(this, plan.ReturnType) is not { Kind: BoundTypeKind.Semantics, Components: [var referent] } result || result.Semantics != semantics || !ReferenceEquals(referent, element) ||
            !ReferenceEquals(SignatureType(this, call.BoundType), result))
        {
            return Fail("Storage capability does not match its element and remainder Types.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, state, out _) || !this.ScalarArrayArgument(body, id, 1, pointer, out var address))
        {
            return Fail("Storage capability arguments are unavailable at the call.", out failure);
        }

        function.AddScalar(EmissionOpcode.BorrowAddress, id, [address]);
        return true;
    }

    private bool LowerStorageRelease(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || plan.ArgumentToParameter.Length != 1 || target.Parameters.Count != 1 ||
            SignatureType(this, plan.ArgumentOperations[0].ParameterType) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components.Count: 1 } pointer ||
            !ReferenceEquals(SignatureType(this, plan.ReturnType), BoundType.Unit) || !ReferenceEquals(SignatureType(this, call.BoundType), BoundType.Unit))
        {
            return Fail("Storage release requires one raw region pointer and a Unit result.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, pointer, out var address) || !this.TryGetLocation(call, directory, constants, out var location))
        {
            return Fail("Storage release argument or location is unavailable at the call.", out failure);
        }

        function.AddCall(id, WindowsLowering.StorageRelease, [address, new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
        return true;
    }

    // SPEC 4.7.2, 4.7.4: Array<T>.init(capacity:) zeroes the result handle and reserves the capacity once; the reserve runtime
    // aborts on a negative capacity, and zero allocates nothing.
    private bool LowerArrayConstruction(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto { IsConstructor: true } target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            call.ArgumentNodes.Count != 1 || plan.ArgumentOperations.Length != 1 || plan.ArgumentToParameter.Length != 1 || target.Parameters.Count != 1 ||
            SignatureType(this, call.BoundType) is not { Kind: BoundTypeKind.Array } arrayType || !this.TryGetArrayElement(arrayType.Components[0], out var element))
        {
            return Fail("Array construction has an unsupported argument plan or element Type.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, BoundType.ISize, out var capacity) || !this.TryGetLocation(call, directory, constants, out var location))
        {
            return Fail("Array construction capacity or location is unavailable at the call.", out failure);
        }

        this.arrayRuntimeUsed = true;
        var handle = new EmissionOperand(EmissionOperandKind.SlotAddress, body.Operations[id].Place);
        function.AddCall(id, WindowsLowering.ArrayInit, [handle, new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
        function.AddCall(id, WindowsLowering.ArrayReserve, [handle, new(EmissionOperandKind.Integer, element.Stride), capacity, new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
        return true;
    }

    private bool PrepareCollectionArguments(OwnershipBody body, int id, InvocationKoto call, BoundCall plan, FunctionKoto target, out bool complete, out string? failure)
    {
        failure = null;
        Grow(ref this.parameterArguments, target.Parameters.Count);
        this.parameterArguments.AsSpan(0, target.Parameters.Count).Fill(-1);
        var cursor = 0;
        complete = true;
        for (var i = plan.Receiver is null ? 0 : -1; i < call.ArgumentNodes.Count; i++)
        {
            var parameter = i < 0 ? 0 : plan.ArgumentToParameter[i];
            var acquisition = i < 0 ? plan.ReceiverOperation : plan.ArgumentOperations[i];
            var sourceArgument = i < 0 ? plan.Receiver : call.ArgumentNodes[i];
            if (sourceArgument is null || (uint)parameter >= (uint)target.Parameters.Count || this.parameterArguments[parameter] != -1 || !ReferenceEquals(acquisition.Source, sourceArgument) ||
                acquisition.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow))
            {
                return Fail("Invalid Collection operation argument mapping or acquisition.", out failure);
            }

            // The builder omits CallEntry for an argument whose evaluation cannot complete.
            if (!this.flow!.Nodes[sourceArgument].CanCompleteNormally)
            {
                this.parameterArguments[parameter] = -2;
                complete = false;
                continue;
            }

            if (cursor == this.arguments.Count)
            {
                return Fail("Missing acquired Collection operation argument.", out failure);
            }

            var entry = this.arguments[cursor++];
            if (!ReferenceEquals(body.Operations[entry].Source, call) || (body.IsReachable(id) && !this.Dominates(entry, id)))
            {
                return Fail("Collection operation entry does not match its call.", out failure);
            }

            this.parameterArguments[parameter] = entry;
        }

        if (cursor != this.arguments.Count || (!complete && body.IsReachable(id)))
        {
            return Fail("Collection operation has extra arguments or follows a noncompleting argument.", out failure);
        }

        this.arguments.Clear();
        return true;
    }

    private bool ArrayIndexArgument(KimiLibrary library, OwnershipBody body, int call, bool indexValue, out EmissionOperand operand)
    {
        if (!indexValue)
        {
            return this.ScalarArrayArgument(body, call, 1, BoundType.ISize, out operand);
        }

        operand = default;
        return library.Index.Type is { } type && this.TryGetArrayElement(type, out var element) &&
            element.Layout is { Fields.Length: 2 } layout && layout.Offset(0) == 0 && layout.Offset(1) == 8 &&
            this.ArrayValueArgument(body, call, 1, element, out operand);
    }

    // A scalar argument (the receiver borrow, an isize index or amount) is the acquired entry's aliased value.
    private bool ScalarArrayArgument(OwnershipBody body, int call, int parameter, BoundType type, out EmissionOperand operand)
    {
        operand = default;
        var entry = this.parameterArguments[parameter];
        if (entry < 0)
        {
            return false;
        }

        var place = body.Operations[entry].Place;
        if (!ReferenceTypes.StorageMatches(type, body.Places[place].Type) || body.Values[entry].Kind != OwnershipValueKind.Alias || body.Values[entry].Count != 1)
        {
            return false;
        }

        var value = Input(body, entry, 0);
        if (ValuePlace(body.Operations[value]) != place || (body.IsReachable(entry) && !this.Dominates(value, entry)) || (body.IsReachable(call) && !this.Dominates(value, call)))
        {
            return false;
        }

        operand = this.PhysicalOperand(body, value);
        return true;
    }

    // An element argument transfers into the buffer: scalars by value, strings and aggregates from their acquired slot.
    private bool ArrayValueArgument(OwnershipBody body, int call, int parameter, in ArrayElement element, out EmissionOperand operand)
    {
        if (element.IsScalar)
        {
            return this.ScalarArrayArgument(body, call, parameter, element.Type, out operand);
        }

        operand = default;
        var entry = this.parameterArguments[parameter];
        if (entry < 0)
        {
            return false;
        }

        var place = body.Operations[entry].Place;
        if (ReferenceEquals(element.Type, BoundType.Unit))
        {
            operand = new(EmissionOperandKind.NullAddress, 0);
            return ReferenceEquals(body.Places[place].Type, BoundType.Unit) &&
                (!body.IsReachable(entry) || (body.GetInputState(entry, place) & PlaceState.MustInit) != 0);
        }

        if (!ReferenceTypes.StorageMatches(element.Type, body.Places[place].Type) || !SlotTypes.IsResult(body.Places[place].Type) ||
            !this.IsSlotValue(body.Places[place]) || (body.IsReachable(entry) && (body.GetInputState(entry, place) & PlaceState.MustInit) == 0))
        {
            return false;
        }

        operand = new(EmissionOperandKind.SlotAddress, place);
        return true;
    }
}
