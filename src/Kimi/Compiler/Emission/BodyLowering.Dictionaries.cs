// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly Dictionary<(DictionaryHelperKind Kind, ValueLowering Key, int KeyLayout, ValueLowering Value, int ValueLayout, int Related), DictionaryHelper> dictionaryHelpers = new();
    private readonly Dictionary<(DictionaryHelperKind Kind, ValueLowering Key, int KeyLayout, ValueLowering Value, int ValueLayout, int Related), DictionaryHelper> dictionaryHelperCache = new();
    private bool dictionaryRuntimeUsed;
    private bool storageBytesUsed;

    internal Func<DictionaryCallee, BoundType, BoundType, int>? RequireDictionaryOperation { get; set; }

    // The kind's row declares the generic DictionaryStorage function its helpers call over the entry Types and names and shapes
    // the helper; the name ends with this pool's helper count.
    private DictionaryHelper GetDictionaryHelper(DictionaryHelperKind kind, in ArrayElement key, in ArrayElement value)
    {
        var row = LegacyRuntimeHelpers.DictionaryRow(kind);
        var related = row.CallsEntries(key.NeedsDestruction || value.NeedsDestruction)
            ? this.RequireDictionaryOperation!(row.Entries, key.Type, value.Type) : -1;
        var cacheKey = (kind, key.Value, key.Layout?.Id ?? -1, value.Value, value.Layout?.Id ?? -1, related);
        if (this.dictionaryHelpers.TryGetValue(cacheKey, out var helper))
        {
            return helper;
        }

        if (!this.dictionaryHelperCache.TryGetValue(cacheKey, out helper))
        {
            var suffix = this.dictionaryHelperCache.Count.ToString(CultureInfo.InvariantCulture);
            var entry = DictionaryEntryLayout.Of(key.Value.Layout, value.Value.Layout); // Independent of what acquires or destroys the values.
            helper = DictionaryHelper.Create(kind, key.ToStored(), value.ToStored(), entry, related, suffix);
            this.dictionaryHelperCache.Add(cacheKey, helper);
        }

        this.dictionaryHelpers.Add(cacheKey, helper);
        return helper;
    }

    private bool LowerDictionaryLiteral(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, int id, out string? failure)
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
            !this.TryGetLocation(operation.Source, constants, out var location))
        {
            return Fail("Dictionary literal requires an initialized handle and matching acquired key/value storage.", out failure);
        }

        if (body.IsReachable(id) && ((body.GetInputState(id, operation.Place) & PlaceState.MustInit) == 0 ||
            (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0 ||
            (inserting && (body.GetInputState(id, valuePlace) & PlaceState.MustInit) == 0)))
        {
            return Fail("Dictionary literal cannot inspect or transfer an uninitialized entry.", out failure);
        }

        var helper = this.GetDictionaryHelper(inserting ? DictionaryHelperKind.Place : DictionaryHelperKind.CheckKey, key, value);
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
        return true;

        EmissionOperand Argument(int place, in ArrayElement element, int operand) => element.IsZeroSized
            ? new(EmissionOperandKind.NullAddress, 0)
            : element.IsScalar ? this.PhysicalOperand(body, Input(body, id, operand)) : new(EmissionOperandKind.SlotAddress, place);
    }

    // SPEC 22.2: the nullable form of Alloc and the unsafe byte copy that the DictionaryStorage capacity bodies use; neither
    // reports a failure, so neither needs a location.
    private bool LowerStorageBytes(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, CallPlan plan, out string? failure)
    {
        failure = null;
        var transfer = plan.Target.CompilerFunction == CompilerFunctionKind.StorageTransferBytes;
        var inputs = transfer ? 3 : 1;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != inputs || call.ArgumentNodes.Count != inputs || target.Parameters.Count != inputs || plan.ArgumentToParameter.Length != inputs ||
            !(transfer ? ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), BoundType.Unit) : BytePointer(body.Resolve(call.BoundType, body.ContextAt(id)))))
        {
            return Fail("A private storage byte primitive requires its fixed signature.", out failure);
        }

        for (var i = 0; i < inputs; i++)
        {
            var parameter = body.Resolve(plan.ArgumentOperations[i].ParameterType, InterpretationContext.Root);
            if (plan.ArgumentToParameter[i] != i || !(i == inputs - 1 ? ReferenceEquals(parameter, BoundType.ISize) : BytePointer(parameter)))
            {
                return Fail("A private storage byte primitive requires its arguments in order.", out failure);
            }
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        this.callOperands.Clear();
        for (var i = 0; i < inputs; i++)
        {
            if (!this.ScalarArrayArgument(body, id, i, body.Resolve(plan.ArgumentOperations[i].ParameterType, InterpretationContext.Root)!, out var operand))
            {
                return Fail("A private storage byte primitive argument is unavailable at the call.", out failure);
            }

            this.callOperands.Add(operand);
        }

        function.AddCall(id, transfer ? RuntimeAbi.TransferBytes : RuntimeAbi.TryAllocateBytes, CollectionsMarshal.AsSpan(this.callOperands));
        this.storageBytesUsed = true;
        return true;

        static bool BytePointer(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] } && ReferenceEquals(pointee, BoundType.Primitives["u8"]);
    }

    // SPEC 4.9.6: placeEntry appends one slot, growing the storage first, and transfers the acquired key and value into
    // it through the literal's typed placement helper. The ordinary source bodies prove that no equal key is stored, and
    // growth failure reports the standard operation's forwarded caller location.
    private bool LowerDictionaryPlacement(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, int id, InvocationKoto call, CallPlan plan, out string? failure)
    {
        failure = null;
        const int inputs = 3;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != inputs || call.ArgumentNodes.Count != inputs || target.Parameters.Count != inputs || plan.ArgumentToParameter.Length != inputs ||
            plan.ArgumentToParameter[0] != 0 || plan.ArgumentToParameter[1] != 1 || plan.ArgumentToParameter[2] != 2 || plan.TypeArguments.Length != 2 ||
            body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] } handleType ||
            pointee.Kind != BoundTypeKind.Dictionary || // D5 exception (owner, 2026-10-22), removed at R6-gen: G160-3's handle states its Dictionary.
            body.Resolve(plan.TypeArguments[0], InterpretationContext.Root) is not { } keyType || body.Resolve(plan.TypeArguments[1], InterpretationContext.Root) is not { } valueType ||
            !ReferenceEquals(body.Resolve(plan.ArgumentOperations[1].ParameterType, InterpretationContext.Root), keyType) ||
            !ReferenceEquals(body.Resolve(plan.ArgumentOperations[inputs - 1].ParameterType, InterpretationContext.Root), valueType) ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value) ||
            !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), BoundType.Unit))
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

        if (!this.ScalarArrayArgument(body, id, 0, handleType, out var handle) ||
            !this.ArrayValueArgument(body, id, 1, key, out var keyArgument) || !this.ArrayValueArgument(body, id, 2, value, out var valueArgument))
        {
            return Fail("Dictionary placement inputs are not acquired values.", out failure);
        }

        this.callOperands.Clear();
        this.callOperands.Add(handle);
        this.callOperands.Add(keyArgument);
        this.callOperands.Add(valueArgument);
        if (function.Abi.CallerLocation)
        {
            this.callOperands.Add(new(EmissionOperandKind.CallerLocation, 0));
            this.callOperands.Add(new(EmissionOperandKind.CallerLocationLength, 0));
        }
        else
        {
            if (!this.TryGetLocation(call, constants, out var location))
            {
                return Fail("Dictionary placement has no diagnostic source location.", out failure);
            }

            this.callOperands.Add(new(EmissionOperandKind.ConstantAddress, location));
            this.callOperands.Add(new(EmissionOperandKind.ConstantLength, location));
        }

        function.AddCall(id, this.GetDictionaryHelper(DictionaryHelperKind.Place, key, value).Abi, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }
}
