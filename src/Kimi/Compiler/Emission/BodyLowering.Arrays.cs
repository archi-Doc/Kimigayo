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

    private readonly record struct ArrayElement(BoundType Type, ValueLowering Value, AggregateLayout? Layout, bool IsString)
    {
        internal bool IsScalar => this.Layout is null && !this.IsString && !ReferenceEquals(this.Type, BoundType.Unit);

        // A zero-sized value (Unit or an empty aggregate) has no slot, so its helpers take no value or result pointer.
        internal bool IsZeroSized => this.Value.Layout.Size == 0;

        internal bool NeedsDestruction => this.IsString || this.Layout?.NeedsDestruction == true;

        internal long Stride => this.Value.Layout.Stride;
    }

    // SPEC 4.3: an Array literal's scalar payloads are stored in their own slots so construction can move their bytes into the buffer.
    private bool IsArrayPayload(OwnershipBody body, int place)
        => body.Places[place].Kind == OwnershipPlaceKind.Payload && this.payloadOwners[place] >= 0 &&
            (body.Places[body.Constructions[this.payloadOwners[place]].Place].Type.Kind == BoundTypeKind.Array ||
             body.Places[body.Constructions[this.payloadOwners[place]].Place].Source is ArrayLiteralKoto { FillLength: not null });

    // SPEC 4.5, 16.3.2: the same recursive destruction applies in fields, payloads and collection slots.
    private string? CollectionFieldDrop(BoundType collection)
    {
        if (collection.Kind == BoundTypeKind.Array && collection.Components.Count == 1 && this.TryGetArrayElement(collection.Components[0], out var element))
        {
            return this.GetArrayHelper(ArrayHelperKind.Drop, element).Abi.Name;
        }

        return collection.Kind == BoundTypeKind.Dictionary && collection.Components.Count == 2 &&
            this.TryGetArrayElement(collection.Components[0], out var key) && this.TryGetArrayElement(collection.Components[1], out var value)
            ? this.GetDictionaryHelper(DictionaryHelperKind.Drop, key, value).Abi.Name : null;
    }

    // SPEC 4.5: every supported complete element Type has a recursive storage plan; a zero-sized element has stride zero,
    // and its Array keeps the substitute buffer of the capacity routines.
    private bool TryGetArrayElement(BoundType type, out ArrayElement element)
    {
        element = default;
        if (ReferenceEquals(type, BoundType.Unit))
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

        if (ReferenceEquals(type, BoundType.Never) || this.aggregateLayouts.GetStored(type) is not { } layout)
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
            ArrayHelperKind.Pop => "__kimi_array_pop_",
            ArrayHelperKind.Remove => "__kimi_array_remove_",
            ArrayHelperKind.Place => "__kimi_array_place_",
            ArrayHelperKind.Clear => "__kimi_array_clear_",
            ArrayHelperKind.Swap => "__kimi_array_swap_",
            ArrayHelperKind.BorrowStorage => "__kimi_array_borrow_",
            ArrayHelperKind.OwnStorage => "__kimi_array_own_",
            ArrayHelperKind.OwnFixedStorage => "__kimi_fixed_own_",
            _ => "__kimi_array_drop_",
        };
        var records = remainder is null ? string.Empty : "_r" + remainder.Id.ToString(CultureInfo.InvariantCulture) + (option is null ? string.Empty : "_o" + option.Id.ToString(CultureInfo.InvariantCulture));
        var name = prefix + suffix + records;
        var valueType = element.IsScalar ? element.Value.ComputationType : "ptr";
        var handle = new AbiParameter("ptr", "handle");
        var location = new AbiParameter("ptr", "location", AbiParameterKind.Location);
        var length = new AbiParameter("i64", "location_length", AbiParameterKind.LocationLength);
        var unit = WindowsLowering.Unit.ComputationType;
        var indexParameter = new AbiParameter("i64", "index");
        FunctionAbi abi = kind switch
        {
            ArrayHelperKind.Append when element.IsZeroSized => new(name, unit, [handle, location, length]),
            ArrayHelperKind.Append => new(name, unit, [handle, new(valueType, "value"), location, length]),
            ArrayHelperKind.Insert when element.IsZeroSized => new(name, unit, [handle, indexParameter, location, length]),
            ArrayHelperKind.Insert => new(name, unit, [handle, indexParameter, new(valueType, "value"), location, length]),
            ArrayHelperKind.Pop => new(name, unit, [handle, new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            ArrayHelperKind.Place when element.IsZeroSized => new(name, unit, [handle, location, length]),
            ArrayHelperKind.Place => new(name, unit, [handle, new("ptr", "source"), location, length]),
            ArrayHelperKind.Remove => element.IsScalar
                ? new(name, element.Value.ComputationType, [handle, indexParameter, location, length])
                : element.IsZeroSized
                ? new(name, unit, [handle, indexParameter, location, length])
                : new(name, unit, [handle, indexParameter, new("ptr", "result", AbiParameterKind.ResultSlot), location, length], resultSlot: true),
            ArrayHelperKind.Swap => new(name, unit, [handle, new("i64", "first"), new("i64", "second"), location, length]),
            ArrayHelperKind.BorrowStorage => new(name, unit, [handle, new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            ArrayHelperKind.OwnStorage => new(name, unit, [new("ptr", "value"), new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            ArrayHelperKind.OwnFixedStorage when remainder!.Fields[0].Layout.Size == 0 => new(name, unit, [new("i64", "count"), new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            ArrayHelperKind.OwnFixedStorage => new(name, unit, [new("ptr", "value"), new("i64", "count"), new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
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
            body.Resolve(plan.ReceiverOperation.ParameterType, InterpretationContext.Root) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq } receiverType ||
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
        if (kind != CompilerFunctionKind.ArrayPop && !function.Abi.CallerLocation && !this.TryGetLocation(call, directory, constants, out location))
        {
            return Fail("An Array operation has no diagnostic source location.", out failure);
        }

        var returnType = body.Resolve(plan.ReturnType, InterpretationContext.Root);
        if (!ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), returnType) || returnType is null)
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
                var insert = kind != CompilerFunctionKind.ArrayAppend;
                callee = this.GetArrayHelper(insert ? ArrayHelperKind.Insert : ArrayHelperKind.Append, element).Abi;
                if (insert)
                {
                    if (!this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var index))
                    {
                        return Fail("Array insert index is unavailable at the call.", out failure);
                    }

                    this.callOperands.Add(index);
                }

                if (!this.ArrayValueArgument(body, id, insert ? 2 : 1, element, out var value))
                {
                    return Fail("Array element argument is not an initialized acquired value.", out failure);
                }

                if (!element.IsZeroSized)
                {
                    this.callOperands.Add(value);
                }

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
                callee = this.GetArrayHelper(ArrayHelperKind.Remove, element).Abi;
                if (!this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var removed))
                {
                    return Fail("Array remove index is unavailable at the call.", out failure);
                }

                this.callOperands.Add(removed);
                if (element.IsZeroSized)
                {
                    // A removed zero-sized value has no bytes and no slot: the helper returns nothing to place.
                    if (!ReferenceTypes.StorageMatches(element.Type, returnType))
                    {
                        return Fail("Array remove result does not match its element Type.", out failure);
                    }
                }
                else if (element.IsScalar)
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

        if (kind != CompilerFunctionKind.ArrayPop)
        {
            this.callOperands.Add(new(function.Abi.CallerLocation ? EmissionOperandKind.CallerLocation : EmissionOperandKind.ConstantAddress, location));
            this.callOperands.Add(new(function.Abi.CallerLocation ? EmissionOperandKind.CallerLocationLength : EmissionOperandKind.ConstantLength, location));
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
        if (kind == CompilerFunctionKind.StorageSetArrayLength)
        {
            return this.LowerArrayLength(body, function, id, call, plan, out failure);
        }

        if (kind == CompilerFunctionKind.StorageOwnFixed)
        {
            return this.LowerFixedStorageOwn(body, function, id, call, plan, out failure);
        }

        if (kind == CompilerFunctionKind.StorageDictionaryLayout)
        {
            return this.LowerDictionaryLayout(body, function, id, call, plan, out failure);
        }

        if (kind == CompilerFunctionKind.StoragePlaceDictionaryEntry)
        {
            return this.LowerDictionaryPlacement(body, function, constants, directory, id, call, plan, out failure);
        }

        if (kind is CompilerFunctionKind.StorageTryAllocateBytes or CompilerFunctionKind.StorageTransferBytes)
        {
            return this.LowerStorageBytes(body, function, id, call, plan, out failure);
        }

        if (kind is CompilerFunctionKind.StorageIndexBounds or CompilerFunctionKind.StorageMissingDictionaryKey or CompilerFunctionKind.StorageArgumentOutOfRange or CompilerFunctionKind.StorageCountOverflow or CompilerFunctionKind.StorageAllocationSizeExceeded)
        {
            if (!function.Abi.CallerLocation || plan.ArgumentOperations.Length != 0 || plan.Receiver is not null || !ReferenceEquals(plan.ReturnType, BoundType.Never))
            {
                return Fail("A standard precondition failure requires its entry's forwarded caller location.", out failure);
            }

            var reason = kind switch
            {
                CompilerFunctionKind.StorageIndexBounds => WindowsLowering.IndexBoundsReason,
                CompilerFunctionKind.StorageMissingDictionaryKey => WindowsLowering.MissingKeyReason,
                CompilerFunctionKind.StorageCountOverflow => WindowsLowering.IntegerOverflowReason,
                CompilerFunctionKind.StorageAllocationSizeExceeded => WindowsLowering.AllocationSizeReason,
                _ => WindowsLowering.ArgumentRangeReason,
            };
            function.AddCall(id, WindowsLowering.Abort, [new(EmissionOperandKind.Integer, reason), new(EmissionOperandKind.CallerLocation, 0), new(EmissionOperandKind.CallerLocationLength, 0), new(EmissionOperandKind.Integer, -2)]);
            function.Add(EmissionOpcode.Unreachable, id);
            return true;
        }

        if (kind is CompilerFunctionKind.StorageBorrowDictionary or CompilerFunctionKind.StorageBorrowDictionaryExclusive)
        {
            return this.LowerDictionaryStorageBorrow(body, function, id, call, plan, out failure);
        }

        if (kind is CompilerFunctionKind.StorageBorrowFixedShared or CompilerFunctionKind.StorageBorrowFixedExclusive)
        {
            return this.LowerFixedStorageBorrow(body, function, id, call, plan, out failure);
        }

        if (kind is >= CompilerFunctionKind.StorageOwnDictionary and <= CompilerFunctionKind.StorageValueAt)
        {
            return this.LowerOwnedDictionaryStorage(body, function, id, call, plan, out failure);
        }

        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || plan.ArgumentToParameter.Length != 1 || target.Parameters.Count != 1)
        {
            return Fail("Storage operation has an unsupported argument plan.", out failure);
        }

        // A reference parameter is Copied, Reborrowed or borrowed from its Place, and every form supplies the pointer;
        // ownStorage takes the Array's acquired slot.
        var argumentType = body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root);
        var referent = kind == CompilerFunctionKind.StorageOwn ? argumentType : argumentType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var target0] } ? target0 : null;
        if (referent is null || argumentType is null || plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
            (kind == CompilerFunctionKind.StorageOwn && plan.ArgumentOperations[0].Kind != ArgumentOperationKind.Value))
        {
            return Fail("Storage operation has an unsupported argument acquisition.", out failure);
        }

        if (referent.Components is not [var elementType] || (kind == CompilerFunctionKind.StorageBorrowUniqSlice ? referent.Symbol?.LibraryDeclaration != KimiDeclarationId.UniqSlice : referent.Kind != BoundTypeKind.Array) ||
            !this.TryGetArrayElement(elementType, out var element))
        {
            return Fail("Storage operation has an unsupported collection or element Type.", out failure);
        }

        if (kind == CompilerFunctionKind.StorageBorrowUniqSlice &&
            (this.aggregateLayouts.Get(referent) is not { Fields.Length: 3 } view || view.Offset(0) != 0 || view.Offset(1) != 8 || view.Fields[2].Layout.Size != 0))
        {
            return Fail("An exclusive view must have pointer, length and erased Loan storage.", out failure);
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

        var returnType = body.Resolve(plan.ReturnType, InterpretationContext.Root);
        if (returnType is null || !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), returnType) || this.aggregateLayouts.Get(returnType) is not { } result ||
            !SlotTypes.IsResult(returnType) || !this.ValidateSlotCallResult(body, id, out failure))
        {
            return Fail(failure ?? "Storage operation result is not a stored record.", out failure);
        }

        // The owning helpers and the remainder's drop share the record shape {storage, position, count, capacity}.
        var remainder = result;
        if (remainder is not { IsArray: false } || remainder.Fields.Length != 4 || (!owning && remainder.Fields[3].Layout.Size != 0) ||
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

    // The Kimi caller has initialized the reserved tail. This boundary only publishes its length.
    private bool LowerArrayLength(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 2 || call.ArgumentNodes.Count != 2 || target.Parameters.Count != 2 || plan.ArgumentToParameter.Length != 2 ||
            plan.ArgumentToParameter[0] != 0 || plan.ArgumentToParameter[1] != 1 ||
            body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Array }] } receiver ||
            !ReferenceEquals(body.Resolve(plan.ArgumentOperations[1].ParameterType, InterpretationContext.Root), BoundType.ISize) || !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), BoundType.Unit) || !ReferenceEquals(body.Resolve(plan.ReturnType, InterpretationContext.Root), BoundType.Unit))
        {
            return Fail("Publishing an Array length requires its exclusive handle and isize length.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, receiver, out var handle) || !this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var length))
        {
            return Fail("Array length publication arguments are unavailable at the call.", out failure);
        }

        function.AddScalar(EmissionOpcode.ElementAddress, id, [handle, new(EmissionOperandKind.Integer, 8)], representation: WindowsLowering.GetValue(BoundType.ISize));
        function.AddScalar(EmissionOpcode.StorePointer, id, [length, new(EmissionOperandKind.ElementAddress, id)], "i64", representation: WindowsLowering.GetValue(BoundType.ISize));
        return true;
    }

    // SPEC 4.7.2, 4.7.4: Array<T>.init(capacity:) zeroes the result handle and reserves the capacity once; the reserve runtime
    // aborts on a negative capacity, and zero allocates nothing.
    private bool LowerArrayConstruction(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto { IsConstructor: true } target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            call.ArgumentNodes.Count != 1 || plan.ArgumentOperations.Length != 1 || plan.ArgumentToParameter.Length != 1 || target.Parameters.Count != 1 ||
            body.Resolve(call.BoundType, body.ContextAt(id)) is not { Kind: BoundTypeKind.Array } arrayType || !this.TryGetArrayElement(arrayType.Components[0], out var element))
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

        var location = -1;
        if (!this.ScalarArrayArgument(body, id, 0, BoundType.ISize, out var capacity) || (!function.Abi.CallerLocation && !this.TryGetLocation(call, directory, constants, out location)))
        {
            return Fail("Array construction capacity or location is unavailable at the call.", out failure);
        }

        this.arrayRuntimeUsed = true;
        var handle = new EmissionOperand(EmissionOperandKind.SlotAddress, body.Operations[id].Place);
        var place = new EmissionOperand(function.Abi.CallerLocation ? EmissionOperandKind.CallerLocation : EmissionOperandKind.ConstantAddress, location);
        var length = new EmissionOperand(function.Abi.CallerLocation ? EmissionOperandKind.CallerLocationLength : EmissionOperandKind.ConstantLength, location);
        function.AddCall(id, WindowsLowering.ArrayInit, [handle, place, length]);
        function.AddCall(id, WindowsLowering.ArrayReserve, [handle, new(EmissionOperandKind.Integer, element.Stride), capacity, place, length]);
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
