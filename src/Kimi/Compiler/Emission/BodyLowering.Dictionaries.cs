// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly Dictionary<(DictionaryHelperKind Kind, ValueLowering Key, int KeyLayout, ValueLowering Value, int ValueLayout, int Result, string Related), DictionaryHelper> dictionaryHelpers = new();
    private readonly Dictionary<(DictionaryHelperKind Kind, ValueLowering Key, int KeyLayout, ValueLowering Value, int ValueLayout, int Result, string Related), DictionaryHelper> dictionaryHelperCache = new();
    private bool dictionaryRuntimeUsed;

    private static long AlignDictionary(long size, int alignment) => (size + alignment - 1) & -(long)alignment;

    // Physical entry metadata is independent of the operations that acquire or destroy its stored values.
    private static (long KeyOffset, long ValueOffset, long Stride) GetDictionaryEntryLayout(in ArrayElement key, in ArrayElement value)
    {
        var alignment = Math.Max(8, Math.Max(key.Value.Layout.Alignment, value.Value.Layout.Alignment));
        var keyOffset = AlignDictionary(16, key.Value.Layout.Alignment);
        var valueOffset = AlignDictionary(keyOffset + key.Value.Layout.Size, value.Value.Layout.Alignment);
        return (keyOffset, valueOffset, AlignDictionary(valueOffset + value.Value.Layout.Size, alignment));
    }

    private DictionaryHelper GetDictionaryHelper(DictionaryHelperKind kind, in ArrayElement key, in ArrayElement value, AggregateLayout? result = null, FunctionAbi? equality = null)
    {
        FunctionAbi? related = kind == DictionaryHelperKind.Find ? equality :
            kind == DictionaryHelperKind.Drop ? this.GetDictionaryHelper(DictionaryHelperKind.Clear, key, value).Abi :
            equality is not null ? this.GetDictionaryHelper(DictionaryHelperKind.Find, key, value, equality: equality).Abi : null;
        var cacheKey = (kind, key.Value, key.Layout?.Id ?? -1, value.Value, value.Layout?.Id ?? -1, result?.Id ?? -1, related?.Name ?? string.Empty);
        if (this.dictionaryHelpers.TryGetValue(cacheKey, out var helper))
        {
            return helper;
        }

        if (!this.dictionaryHelperCache.TryGetValue(cacheKey, out helper))
        {
            var name = "__kimi_dictionary_" + kind.ToString().ToLowerInvariant() + this.dictionaryHelperCache.Count.ToString(CultureInfo.InvariantCulture);
            var handle = new AbiParameter("ptr", "handle");
            var location = new AbiParameter("ptr", "location", AbiParameterKind.Location);
            var length = new AbiParameter("i64", "location_length", AbiParameterKind.LocationLength);
            var output = new AbiParameter("ptr", "result", AbiParameterKind.ResultSlot);
            FunctionAbi abi = kind switch
            {
                DictionaryHelperKind.Find => new(name, "i64", [handle, new("ptr", "key")]),
                DictionaryHelperKind.CheckKey => new(name, "void", [handle, new(key.IsScalar ? key.Value.ComputationType : "ptr", "key"), location, length]),
                DictionaryHelperKind.Place => new(name, "void", [handle, new(key.IsScalar ? key.Value.ComputationType : "ptr", "key"), new(value.IsScalar ? value.Value.ComputationType : "ptr", "value"), location, length]),
                DictionaryHelperKind.PlaceValue => new(name, "void", [new("ptr", "slot"), new(value.IsScalar ? value.Value.ComputationType : "ptr", "value")]),
                _ => new(name, "void", [handle, location, length]),
            };
            var layout = GetDictionaryEntryLayout(key, value);
            helper = new(kind, abi, key.Value, key.Layout, key.IsString, value.Value, value.Layout, value.IsString, layout.KeyOffset, layout.ValueOffset, layout.Stride, result, related);
            this.dictionaryHelperCache.Add(cacheKey, helper);
        }

        this.dictionaryHelpers.Add(cacheKey, helper);
        return helper;
    }

    private bool LowerDictionaryLiteral(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var inserting = operation.Kind == OwnershipOperationKind.StoreDictionaryEntry;
        var valuePlace = inserting ? body.OperationSteps[id] : -1;
        if ((uint)operation.Place >= (uint)body.Places.Count || (uint)operation.Input >= (uint)body.Places.Count ||
            body.Places[operation.Place] is not { Type: { Kind: BoundTypeKind.Dictionary, Components.Count: 2 } dictionary, Source: DictionaryLiteralKoto } ||
            !FitsValue(body.Places[operation.Input].Type, dictionary.Components[0], operation.Source) ||
            (inserting && ((uint)valuePlace >= (uint)body.Places.Count || !FitsValue(body.Places[valuePlace].Type, dictionary.Components[1], operation.Source))) ||
            !this.TryGetArrayElement(dictionary.Components[0], out var key) || !this.TryGetArrayElement(dictionary.Components[1], out var value) ||
            !this.TryGetLocation(operation.Source, directory, constants, out var location))
        {
            return Fail("Dictionary literal requires an initialized handle and matching acquired key/value storage.", out failure);
        }

        if (body.IsReachable(id) && ((body.GetInputState(id, operation.Place) & PlaceState.MustInit) == 0 ||
            (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0 ||
            (inserting && (body.GetInputState(id, valuePlace) & PlaceState.MustInit) == 0)))
        {
            return Fail("Dictionary literal cannot inspect or transfer an uninitialized entry.", out failure);
        }

        FunctionAbi? equality = null;
        if (!inserting && (operation.Source.CodeContext.Compilation.Binding.DictionaryComparison(dictionary) is not { } comparison ||
            (equality = this.ComparisonHelpers?.GetValueOrDefault(comparison)) is null))
        {
            return Fail("Dictionary literal requires a finalized equality witness.", out failure);
        }

        var helper = this.GetDictionaryHelper(inserting ? DictionaryHelperKind.Place : DictionaryHelperKind.CheckKey, key, value, equality: equality);
        this.callOperands.Clear();
        this.callOperands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
        this.callOperands.Add(Argument(operation.Input, key, 0));
        if (inserting)
        {
            this.callOperands.Add(Argument(valuePlace, value, 1));
        }

        this.callOperands.Add(new(EmissionOperandKind.ConstantAddress, location));
        this.callOperands.Add(new(EmissionOperandKind.ConstantLength, location));
        function.AddCall(id, helper.Abi, CollectionsMarshal.AsSpan(this.callOperands));
        this.dictionaryRuntimeUsed = true;
        this.arrayRuntimeUsed = true;
        return true;

        EmissionOperand Argument(int place, in ArrayElement element, int operand) => element.IsZeroSized
            ? new(EmissionOperandKind.NullAddress, 0)
            : element.IsScalar ? this.PhysicalOperand(body, Input(body, id, operand)) : new(EmissionOperandKind.SlotAddress, place);
    }

    // SPEC 4.7.4: reserveEntries and shrinkEntries run the Kimigayo capacity decisions of DictionaryStorage.kimi over
    // compiler-constructed platform callbacks; failure reports the standard operation's forwarded caller location.
    private bool LowerDictionaryCapacity(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var reserve = plan.Target.CompilerFunction == CompilerFunctionKind.StorageReserveDictionary;
        var inputs = reserve ? 2 : 1;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != inputs || call.ArgumentNodes.Count != inputs || target.Parameters.Count != inputs || plan.ArgumentToParameter.Length != inputs ||
            plan.ArgumentToParameter[0] != 0 || (reserve && plan.ArgumentToParameter[1] != 1) ||
            SignatureType(this, plan.ArgumentOperations[0].ParameterType) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Dictionary, Components: [var keyType, var valueType] }] } input ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value) ||
            (reserve && !ReferenceEquals(SignatureType(this, plan.ArgumentOperations[1].ParameterType), BoundType.ISize)) ||
            !ReferenceEquals(SignatureType(this, call.BoundType), BoundType.Unit))
        {
            return Fail("Dictionary capacity operation requires its exclusive Dictionary and entry layout.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, input, out var handle))
        {
            return Fail("Dictionary capacity operation has no acquired handle.", out failure);
        }

        this.callOperands.Clear();
        this.callOperands.Add(handle);
        this.callOperands.Add(new(EmissionOperandKind.Integer, GetDictionaryEntryLayout(key, value).Stride));
        if (reserve)
        {
            if (!this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var additional))
            {
                return Fail("Dictionary reserve amount is unavailable.", out failure);
            }

            this.callOperands.Add(additional);
        }

        if (function.Abi.CallerLocation)
        {
            this.callOperands.Add(new(EmissionOperandKind.CallerLocation, 0));
            this.callOperands.Add(new(EmissionOperandKind.CallerLocationLength, 0));
        }
        else
        {
            if (!this.TryGetLocation(call, directory, constants, out var location))
            {
                return Fail("Dictionary capacity operation has no diagnostic source location.", out failure);
            }

            this.callOperands.Add(new(EmissionOperandKind.ConstantAddress, location));
            this.callOperands.Add(new(EmissionOperandKind.ConstantLength, location));
        }

        this.dictionaryRuntimeUsed = true;
        this.arrayRuntimeUsed = true;
        function.AddCall(id, reserve ? WindowsLowering.DictionaryReserve : WindowsLowering.DictionaryShrink, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }

    // SPEC 4.7.5: placeEntry appends one slot, growing the storage first, and transfers the acquired key and value into
    // it through the literal's typed placement helper. The ordinary source bodies prove that no equal key is stored, and
    // growth failure reports the standard operation's forwarded caller location.
    private bool LowerDictionaryPlacement(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var entry = plan.Target.CompilerFunction == CompilerFunctionKind.StoragePlaceDictionaryEntry;
        var inputs = entry ? 3 : 2;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != inputs || call.ArgumentNodes.Count != inputs || target.Parameters.Count != inputs || plan.ArgumentToParameter.Length != inputs ||
            plan.ArgumentToParameter[0] != 0 || plan.ArgumentToParameter[1] != 1 || (entry && plan.ArgumentToParameter[2] != 2) || plan.TypeArguments.Length != 2 ||
            SignatureType(this, plan.ArgumentOperations[0].ParameterType) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components: [var pointee] } handleType ||
            !ReferenceEquals(pointee, BoundType.Primitives["u8"]) ||
            SignatureType(this, plan.TypeArguments[0]) is not { } keyType || SignatureType(this, plan.TypeArguments[1]) is not { } valueType ||
            (entry && !ReferenceEquals(SignatureType(this, plan.ArgumentOperations[1].ParameterType), keyType)) ||
            !ReferenceEquals(SignatureType(this, plan.ArgumentOperations[inputs - 1].ParameterType), valueType) ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value) ||
            !ReferenceEquals(SignatureType(this, call.BoundType), BoundType.Unit))
        {
            return Fail("Dictionary placement requires the physical slot and acquired key/value Types.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        EmissionOperand keyArgument = default;
        if (!this.ScalarArrayArgument(body, id, 0, handleType, out var handle) ||
            (entry && !this.ArrayValueArgument(body, id, 1, key, out keyArgument)) || !this.ArrayValueArgument(body, id, inputs - 1, value, out var valueArgument))
        {
            return Fail("Dictionary placement inputs are not acquired values.", out failure);
        }

        this.callOperands.Clear();
        this.callOperands.Add(handle);
        if (!entry)
        {
            this.callOperands.Add(valueArgument);
            function.AddCall(id, this.GetDictionaryHelper(DictionaryHelperKind.PlaceValue, key, value).Abi, CollectionsMarshal.AsSpan(this.callOperands));
            this.dictionaryRuntimeUsed = true;
            this.arrayRuntimeUsed = true;
            return true;
        }

        this.callOperands.Add(keyArgument);
        this.callOperands.Add(valueArgument);
        if (function.Abi.CallerLocation)
        {
            this.callOperands.Add(new(EmissionOperandKind.CallerLocation, 0));
            this.callOperands.Add(new(EmissionOperandKind.CallerLocationLength, 0));
        }
        else
        {
            if (!this.TryGetLocation(call, directory, constants, out var location))
            {
                return Fail("Dictionary placement has no diagnostic source location.", out failure);
            }

            this.callOperands.Add(new(EmissionOperandKind.ConstantAddress, location));
            this.callOperands.Add(new(EmissionOperandKind.ConstantLength, location));
        }

        function.AddCall(id, this.GetDictionaryHelper(DictionaryHelperKind.Place, key, value).Abi, CollectionsMarshal.AsSpan(this.callOperands));
        this.dictionaryRuntimeUsed = true;
        this.arrayRuntimeUsed = true;
        return true;
    }
}
