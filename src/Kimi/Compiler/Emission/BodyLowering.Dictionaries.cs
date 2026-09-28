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
                DictionaryHelperKind.TryInsert or DictionaryHelperKind.InsertOrReplace => new(name, "void", [handle, new(key.IsScalar ? key.Value.ComputationType : "ptr", "key"), new(value.IsScalar ? value.Value.ComputationType : "ptr", "value"), output, location, length], resultSlot: true),
                DictionaryHelperKind.Remove or DictionaryHelperKind.TryGet => new(name, "void", [handle, new("ptr", "key"), output, location, length], resultSlot: true),
                _ => new(name, "void", [handle, location, length]),
            };
            var alignment = Math.Max(8, Math.Max(key.Value.Layout.Alignment, value.Value.Layout.Alignment));
            var keyOffset = AlignDictionary(16, key.Value.Layout.Alignment);
            var valueOffset = AlignDictionary(keyOffset + key.Value.Layout.Size, value.Value.Layout.Alignment);
            helper = new(kind, abi, key.Value, key.Layout, key.IsString, value.Value, value.Layout, value.IsString, keyOffset, valueOffset, AlignDictionary(valueOffset + value.Value.Layout.Size, alignment), result, related);
            this.dictionaryHelperCache.Add(cacheKey, helper);
        }

        this.dictionaryHelpers.Add(cacheKey, helper);
        return helper;
    }

    private bool LowerDictionaryOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var operation = plan.Target.CompilerFunction;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is null || call.AttributeChain is not null ||
            plan.ReceiverOperation.Kind is not (ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != call.ArgumentNodes.Count || plan.ArgumentToParameter.Length != call.ArgumentNodes.Count ||
            call.ArgumentNodes.Count + 1 != target.Parameters.Count || target.BoundSymbol?.ReceiverIndex != 0 ||
            SignatureType(this, plan.ReceiverOperation.ParameterType) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq or SemanticsKind.Ref, Components: [{ Kind: BoundTypeKind.Dictionary, Components.Count: 2 } dictionary] } receiverType ||
            !this.TryGetArrayElement(dictionary.Components[0], out var key, allowEmpty: true) || !this.TryGetArrayElement(dictionary.Components[1], out var value, allowEmpty: true))
        {
            return Fail("Dictionary operation requires a supported receiver, acquired arguments and entry layout.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, receiverType, out var handle) ||
            !ReferenceEquals(SignatureType(this, call.BoundType), SignatureType(this, plan.ReturnType)) ||
            !this.TryGetLocation(call, directory, constants, out var location))
        {
            return Fail("Dictionary operation requires its acquired receiver and matching result.", out failure);
        }

        this.callOperands.Clear();
        this.callOperands.Add(handle);
        FunctionAbi abi;
        if (operation is CompilerFunctionKind.DictionaryReserve or CompilerFunctionKind.DictionaryShrinkToFit)
        {
            abi = operation == CompilerFunctionKind.DictionaryReserve ? WindowsLowering.DictionaryReserve : WindowsLowering.DictionaryShrink;
            this.callOperands.Add(new(EmissionOperandKind.Integer, this.GetDictionaryHelper(DictionaryHelperKind.Clear, key, value).Stride));
            if (operation == CompilerFunctionKind.DictionaryReserve)
            {
                if (!this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var additional))
                {
                    return Fail("Dictionary reserve amount is unavailable.", out failure);
                }

                this.callOperands.Add(additional);
            }
        }
        else if (operation == CompilerFunctionKind.DictionaryClear)
        {
            abi = this.GetDictionaryHelper(DictionaryHelperKind.Clear, key, value).Abi;
        }
        else
        {
            if (this.ComparisonCalls?.GetValueOrDefault(plan) is not { } equality ||
                this.aggregateLayouts.Get(SignatureType(this, plan.ReturnType)!) is not { Cases.Length: 2 } result ||
                !this.ValidateSlotCallResult(body, id, out failure))
            {
                return Fail(failure ?? "Dictionary search requires a finalized equality witness and stored result.", out failure);
            }

            DictionaryHelperKind kind;
            if (operation is CompilerFunctionKind.DictionaryTryInsert or CompilerFunctionKind.DictionaryInsertOrReplace)
            {
                if (!this.ArrayValueArgument(body, id, 1, key, out var keyArgument) || !this.ArrayValueArgument(body, id, 2, value, out var valueArgument))
                {
                    return Fail("Dictionary insertion inputs are not acquired values.", out failure);
                }

                this.callOperands.Add(keyArgument);
                this.callOperands.Add(valueArgument);
                kind = operation == CompilerFunctionKind.DictionaryTryInsert ? DictionaryHelperKind.TryInsert : DictionaryHelperKind.InsertOrReplace;
            }
            else
            {
                var searchType = SignatureType(this, plan.ArgumentOperations[0].ParameterType);
                if (searchType is null || !this.ScalarArrayArgument(body, id, 1, searchType, out var search))
                {
                    return Fail("Dictionary search requires its acquired key borrow.", out failure);
                }

                this.callOperands.Add(search);
                kind = operation == CompilerFunctionKind.DictionaryRemove ? DictionaryHelperKind.Remove : DictionaryHelperKind.TryGet;
            }

            this.callOperands.Add(new(EmissionOperandKind.SlotAddress, body.Operations[id].Place));
            abi = this.GetDictionaryHelper(kind, key, value, result, equality).Abi;
        }

        this.callOperands.Add(new(EmissionOperandKind.ConstantAddress, location));
        this.callOperands.Add(new(EmissionOperandKind.ConstantLength, location));
        this.dictionaryRuntimeUsed = true;
        this.arrayRuntimeUsed = true;
        function.AddCall(id, abi, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }
}
