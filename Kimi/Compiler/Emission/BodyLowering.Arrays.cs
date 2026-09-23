// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>SPEC 4.7.2, 4.7.4, 4.7.6: the Array mutation operations lower to runtime capacity routines and per-element helpers over the {buffer, length, capacity} handle.</summary>
internal sealed partial class BodyLowering
{
    private readonly Dictionary<(ArrayHelperKind Kind, int Layout, string Scalar), ArrayHelper> arrayHelpers = new();

    private readonly record struct ArrayElement(BoundType Type, ValueLowering Value, AggregateLayout? Layout, bool IsString)
    {
        internal bool IsScalar => this.Layout is null && !this.IsString;

        internal bool NeedsDestruction => this.IsString || this.Layout?.NeedsDestruction == true;

        internal long Stride => this.Value.Layout.Stride;
    }

    private bool TryGetArrayElement(BoundType type, out ArrayElement element)
    {
        element = default;
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
        if (type.Kind == BoundTypeKind.Array || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.Never) ||
            this.aggregateLayouts.Get(type) is not { } layout || layout.Value.Layout.Stride <= 0)
        {
            return false;
        }

        element = new(type, layout.Value, layout, false);
        return true;
    }

    private ArrayHelper GetArrayHelper(ArrayHelperKind kind, in ArrayElement element, AggregateLayout? option = null)
    {
        var key = (kind, element.Layout?.Id ?? -1, element.IsString ? "string" : element.IsScalar ? element.Value.ComputationType : string.Empty);
        if (this.arrayHelpers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var suffix = element.Layout is { } layout ? "a" + layout.Id.ToString(CultureInfo.InvariantCulture) : element.IsString ? "string" : element.Value.ComputationType;
        var prefix = kind switch
        {
            ArrayHelperKind.Append => "__kimi_array_append_",
            ArrayHelperKind.Insert => "__kimi_array_insert_",
            ArrayHelperKind.Pop => "__kimi_array_pop_",
            ArrayHelperKind.Remove => "__kimi_array_remove_",
            ArrayHelperKind.Clear => "__kimi_array_clear_",
            _ => "__kimi_array_drop_",
        };
        var name = prefix + suffix;
        var valueType = element.IsScalar ? element.Value.ComputationType : "ptr";
        var handle = new AbiParameter("ptr", "handle");
        var location = new AbiParameter("ptr", "location", AbiParameterKind.Location);
        var length = new AbiParameter("i64", "location_length", AbiParameterKind.LocationLength);
        var unit = WindowsLowering.Unit.ComputationType;
        FunctionAbi abi = kind switch
        {
            ArrayHelperKind.Append => new(name, unit, [handle, new(valueType, "value"), location, length]),
            ArrayHelperKind.Insert => new(name, unit, [handle, new("i64", "index"), new(valueType, "value"), location, length]),
            ArrayHelperKind.Pop => new(name, unit, [handle, new("ptr", "result", AbiParameterKind.ResultSlot)], resultSlot: true),
            ArrayHelperKind.Remove => element.IsScalar
                ? new(name, element.Value.ComputationType, [handle, new("i64", "index"), location, length])
                : new(name, unit, [handle, new("i64", "index"), new("ptr", "result", AbiParameterKind.ResultSlot), location, length], resultSlot: true),
            _ => new(name, unit, [handle, location, length]),
        };
        var helper = new ArrayHelper(kind, abi, element.Value, element.Layout, element.IsString, option);
        this.arrayHelpers.Add(key, helper);
        return helper;
    }

    private bool LowerArrayOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var kind = plan.Target.CompilerFunction;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is null || call.AttributeChain is not null ||
            plan.ReceiverOperation.Kind is not (ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != call.ArgumentNodes.Count || plan.ArgumentToParameter.Length != call.ArgumentNodes.Count ||
            call.ArgumentNodes.Count + 1 != target.Parameters.Count || target.BoundSymbol?.ReceiverIndex != 0 ||
            plan.Receiver.BoundType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq } receiverType ||
            receiverType.Components[0] is not { Kind: BoundTypeKind.Array } arrayType || !this.TryGetArrayElement(arrayType.Components[0], out var element))
        {
            return Fail("Array operation has an unsupported receiver, argument plan or element Type.", out failure);
        }

        Grow(ref this.parameterArguments, target.Parameters.Count);
        this.parameterArguments.AsSpan(0, target.Parameters.Count).Fill(-1);
        var cursor = 0;
        var complete = true;
        for (var i = -1; i < call.ArgumentNodes.Count; i++)
        {
            var parameter = i < 0 ? 0 : plan.ArgumentToParameter[i];
            var acquisition = i < 0 ? plan.ReceiverOperation : plan.ArgumentOperations[i];
            var sourceArgument = i < 0 ? plan.Receiver : call.ArgumentNodes[i];
            if ((uint)parameter >= (uint)target.Parameters.Count || this.parameterArguments[parameter] != -1 || !ReferenceEquals(acquisition.Source, sourceArgument) ||
                acquisition.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow))
            {
                return Fail("Invalid Array operation argument mapping or acquisition.", out failure);
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
                return Fail("Missing acquired Array operation argument.", out failure);
            }

            var entry = this.arguments[cursor++];
            if (!ReferenceEquals(body.Operations[entry].Source, call) || (body.IsReachable(id) && !this.Dominates(entry, id)))
            {
                return Fail("Array operation entry does not match its call.", out failure);
            }

            this.parameterArguments[parameter] = entry;
        }

        if (cursor != this.arguments.Count || (!complete && body.IsReachable(id)))
        {
            return Fail("Array operation has extra arguments or follows a noncompleting argument.", out failure);
        }

        this.arguments.Clear();
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
                var insert = kind == CompilerFunctionKind.ArrayInsert;
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
                callee = this.GetArrayHelper(ArrayHelperKind.Remove, element).Abi;
                if (!this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var removed))
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
        if (!ReferenceTypes.StorageMatches(element.Type, body.Places[place].Type) || !SlotTypes.IsResult(body.Places[place].Type) ||
            !this.IsSlotValue(body.Places[place]) || (body.IsReachable(entry) && (body.GetInputState(entry, place) & PlaceState.MustInit) == 0))
        {
            return false;
        }

        operand = new(EmissionOperandKind.SlotAddress, place);
        return true;
    }
}
