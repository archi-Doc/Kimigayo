// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<EmissionOperand> callOperands = new();
    private Dictionary<FunctionKoto, FunctionAbi>? functions;
    private ControlFlowAnalysis? flow;
    private int[] parameterArguments = [];

    // SPEC 7.2.3: reads and borrows in defaults name acquired slots of a pending call. Index calls once, before validating
    // references or aggregate storage; a default may itself call other functions before the enclosing call enters.
    private void PrepareCallIndex(OwnershipBody body)
    {
        Grow(ref this.elementNextCalls, body.Operations.Count);
        var nextCall = -1;
        for (var i = body.Operations.Count - 1; i >= 0; i--)
        {
            this.elementNextCalls[i] = nextCall;
            if (body.Operations[i].Kind == OwnershipOperationKind.Call)
            {
                nextCall = i;
            }
        }
    }

    private bool ValidateCallArgumentStorage(OwnershipBody body, int entry, int call, out string? failure)
    {
        failure = null;
        var place = body.Operations[entry].Place;
        var type = body.Places[place].Type;
        if (IsScalar(type) && (body.Values[entry].Kind != OwnershipValueKind.Alias || body.Values[entry].Count != 1 ||
            ValuePlace(body.Operations[Input(body, entry, 0)]) != place ||
            (body.IsReachable(entry) && !this.Dominates(Input(body, entry, 0), entry))))
        {
            return Fail("Call argument value is unavailable at acquisition.", out failure);
        }

        if (SlotTypes.IsResult(type) && (!this.IsSlotValue(body.Places[place]) ||
            (body.IsReachable(entry) && (body.GetInputState(entry, place) & PlaceState.MustInit) == 0)))
        {
            return Fail("Owned argument is not an initialized acquired value.", out failure);
        }

        if (body.IsReachable(call) && !this.Dominates(entry, call))
        {
            return Fail("Logical call argument does not dominate the call.", out failure);
        }

        return !(SlotTypes.IsResult(type) && place == body.Operations[call].Place) ||
            Fail("A call cannot share its argument and result storage.", out failure);
    }

    private bool TryCallArgumentOperand(OwnershipBody body, int entry, int call, BoundType type, AbiParameterKind kind, out EmissionOperand operand, out string? failure)
    {
        operand = default;
        failure = null;
        if (kind == AbiParameterKind.Value && IsScalar(type))
        {
            var value = Input(body, entry, 0);
            if (body.IsReachable(call) && !this.Dominates(value, call))
            {
                return Fail("Call argument does not dominate the call.", out failure);
            }

            operand = this.PhysicalOperand(body, value);
        }
        else if (kind is AbiParameterKind.SharedReference or AbiParameterKind.Value && ReferenceTypes.IsString(type))
        {
            operand = this.ReferenceOperand(body, entry);
        }
        else if (kind == AbiParameterKind.OwnedSlot && SlotTypes.IsResult(type))
        {
            operand = new(EmissionOperandKind.SlotAddress, body.Operations[entry].Place);
        }
        else
        {
            return Fail("Unsupported physical call argument.", out failure);
        }

        return true;
    }

    internal IReadOnlyDictionary<BoundCall, GenericStoragePlan.CallEntry>? GenericCalls { get; set; }

    internal IReadOnlyDictionary<BoundCall, FunctionAbi>? FormattingCalls { get; set; }

    internal IReadOnlyDictionary<BoundCall, FunctionAbi>? ComparisonCalls { get; set; }

    internal IReadOnlyDictionary<BoundComparison, FunctionAbi>? ComparisonHelpers { get; set; }

    internal IReadOnlyDictionary<BoundCall, ObjectCall>? ObjectCalls { get; set; }

    internal IReadOnlyDictionary<BoundType, int>? ObjectRuntimeTypes { get; set; }

    internal DefaultGenerationPlan? Defaults { get; set; }

    internal CompilerFunctionAdapters? CompilerEntries { get; set; }

    internal void ClearFunctionContext()
    {
        this.matchBody = null;
        this.matchDefaultContext = -1;
        this.functions = null;
        this.GenericCalls = null;
        this.FormattingCalls = null;
        this.ComparisonCalls = null;
        this.ComparisonHelpers = null;
        this.ObjectCalls = null;
        this.ObjectRuntimeTypes = null;
        this.Defaults = null;
        this.flow = null;
        this.arguments.Clear();
        this.aggregateLayouts.Clear();
    }

    private bool CannotCompleteCall(InvocationKoto call) => !this.flow!.Nodes[call].CanCompleteNormally;

    // A forwarded generic call inside a monomorphized instance binds to the callee's own instance entry (SPEC 21.3.1).
    private GenericStoragePlan.CallEntry? ForwardedEntry(BoundCall call)
    {
        if (this.instanceEntry is not { } entry)
        {
            return null;
        }

        var index = Array.IndexOf(entry.Template.DirectCalls, call);
        return index < 0 ? null : entry.Direct[index];
    }

    private bool LowerCall(KimiLibrary library, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (body.Values[id].Kind == OwnershipValueKind.DefaultCall)
        {
            return this.LowerDefaultCall(body, function, id, out failure);
        }

        var original = body.CallAt(id);
        if (operation.Source is InvocationKoto { BoundValueCall: { } valueCall } invocation)
        {
            return this.LowerValueCall(body, function, constants, directory, id, invocation, valueCall, out failure);
        }

        if (operation.Source is InvocationKoto arrayCall && original is { } arrayPlan && KimiLibraryCatalog.IsArrayOperation(arrayPlan.Target.CompilerFunction))
        {
            return this.LowerArrayOperation(library, body, function, constants, directory, id, arrayCall, arrayPlan, out failure);
        }

        if (operation.Source is InvocationKoto constructionCall && original is { Target.CompilerFunction: CompilerFunctionKind.ArrayWithCapacity } constructionPlan)
        {
            return this.LowerArrayConstruction(body, function, constants, directory, id, constructionCall, constructionPlan, out failure);
        }

        var generic = original is { } bound ? this.GenericCalls?.GetValueOrDefault(bound) ?? this.ForwardedEntry(bound) : null;
        var directIndex = original is not null && this.instanceEntry?.ConcreteCalls is not null ? Array.IndexOf(this.instanceEntry.Template.DirectCalls, original) : -1;
        var resolved = directIndex >= 0 ? this.instanceEntry!.ConcreteCalls![directIndex] : original;
        ObjectCall? creation = resolved is not null && this.ObjectCalls is { } objects && objects.TryGetValue(resolved, out var objectCreation) ? objectCreation : null;
        if (operation.Source is InvocationKoto rawCall && resolved is { } rawPlan && KimiLibraryCatalog.IsRawOperation(rawPlan.Target.CompilerFunction))
        {
            return this.LowerRawOperation(body, function, constants, directory, id, rawCall, rawPlan, out failure);
        }

        if (operation.Source is InvocationKoto storageCall && resolved is { } storagePlan && KimiLibraryCatalog.IsStorageOperation(storagePlan.Target.CompilerFunction))
        {
            return this.LowerStorageOperation(body, function, constants, directory, id, storageCall, storagePlan, out failure);
        }

        var formatting = resolved?.Target.CompilerFunction is >= CompilerFunctionKind.TextFixed and <= CompilerFunctionKind.BuiltinFormat;
        var comparison = resolved?.Target.CompilerFunction is CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare;
        var arithmetic = resolved?.Target.CompilerFunction == CompilerFunctionKind.BuiltinArithmetic;
        var clone = resolved?.Target.CompilerFunction == CompilerFunctionKind.Clone;
        var intrinsic = formatting || comparison || arithmetic || clone;
        if (operation.Source is not InvocationKoto { AttributeChain: null } call || resolved is not { } plan ||
            (!intrinsic && generic is null && creation is null && plan.TypeArguments.Length != 0) ||
            plan.Target.Declaration is not FunctionKoto target || plan.ArgumentOperations.Length != call.ArgumentNodes.Count ||
            plan.ArgumentToParameter.Length != call.ArgumentNodes.Count || call.ArgumentNodes.Count + plan.DefaultArguments.Length + (plan.Receiver is null ? 0 : 1) != target.Parameters.Count ||
            !ReferenceEquals(body.ConcreteAt(ElementAccess.PlaceCallReference(call) ?? call.BoundType, id), SignatureType(this, plan.ReturnType)) || SignatureType(this, plan.ReturnType) is not { } returnType ||
            !ReferenceTypes.StorageMatches(intrinsic ? SignatureType(this, plan.ReturnType) : generic?.Result ?? creation?.Result ?? (target.IsConstructor ? plan.DeclaringType : target.BoundSymbol?.Type), returnType))
        {
            return Fail("A call needs unsupported callee, argument acquisition or result lowering.", out failure);
        }

        var runtime = formatting || clone || ReferenceEquals(plan.Target, library.WriteLine) || ReferenceEquals(plan.Target, library.Abort) || ReferenceEquals(plan.Target, library.GetSymbol(KimiDeclarationId.TestTempDirectory));
        if (clone && (plan.ArgumentOperations.Length != 1 || SignatureType(this, plan.ArgumentOperations[0].ParameterType) is not { Semantics: SemanticsKind.Ref, Components.Count: 1 } borrowedHandle ||
            !ReferenceTypes.StorageMatches(borrowedHandle.Components[0], returnType)))
        {
            return Fail("Strong clone requires a shared handle-slot borrow with the same result mode and payload.", out failure);
        }

        // A selected explicit specialization (SPEC 21.3.4) is called directly; its ABI is the entry's ABI.
        var callee = clone ? ObjectTypes.HandleMode(returnType)?.Counting switch { ObjectCountingStep.NonAtomic => WindowsLowering.CloneRc, ObjectCountingStep.Atomic => WindowsLowering.CloneArc, _ => null }
            : runtime ? WindowsLowering.GetCompilerFunction(plan.Target.CompilerFunction) : creation?.Physical.Abi ?? generic?.Selected ?? generic?.Abi ?? this.functions!.GetValueOrDefault(target);
        var virtualSlot = -1;
        var virtualReceiver = -1;
        if (target.IsVirtual)
        {
            if (plan.VirtualDispatch is not { } dispatch || this.Virtuals is null)
            {
                return Fail("A virtual call has no verified slot selection.", out failure);
            }

            if (dispatch.IsDirect)
            {
                callee = dispatch.Implementation is { } implementation ? this.Virtuals.Entry(implementation, dispatch.ImplementingType) : null;
            }
            else if (!this.Virtuals.TrySlot(target, callee, out virtualSlot, out virtualReceiver))
            {
                return Fail("A virtual call has no physical slot and receiver mapping.", out failure);
            }
        }

        if (plan.Target.CompilerFunction == CompilerFunctionKind.WriterWrite && call.Parent is InterpolatedStringKoto { Formatting: { } formattingRoot } &&
            call.ArgumentNodes.Count == 2 && call.ArgumentNodes[1] is StringLiteralKoto && this.EstimateFormatting(formattingRoot).Capacity == 0)
        {
            callee = LiteralFormatting;
        }

        if (plan.Target.CompilerFunction is CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat && this.FormattingCalls?.GetValueOrDefault(plan) is { } userFormat)
        {
            callee = userFormat;
        }

        if (callee is null && !comparison && !arithmetic)
        {
            return Fail("Call target has no selected implementation ABI.", out failure);
        }

        Grow(ref this.parameterArguments, target.Parameters.Count);
        this.parameterArguments.AsSpan(0, target.Parameters.Count).Fill(-1);
        var cursor = 0;
        var complete = true;
        var previousDefault = -1;
        for (var i = plan.Receiver is null ? 0 : -1; i < call.ArgumentNodes.Count + plan.DefaultArguments.Length; i++)
        {
            var isDefault = i >= call.ArgumentNodes.Count;
            var omitted = isDefault ? plan.DefaultArguments[i - call.ArgumentNodes.Count] : default;
            var parameter = i < 0 ? target.BoundSymbol!.ReceiverIndex : isDefault ? omitted.Parameter.Slot : plan.ArgumentToParameter[i];
            var acquisition = i < 0 ? plan.ReceiverOperation : isDefault
                ? new BoundArgumentOperation(omitted.Expression, omitted.Expression.BoundType, omitted.ParameterType, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: parameter)
                : plan.ArgumentOperations[i];
            var sourceArgument = i < 0 ? plan.Receiver! : isDefault ? omitted.Expression : call.ArgumentNodes[i];
            var parameterType = body.ConcreteAt(acquisition.ParameterType, id);
            if ((uint)parameter >= (uint)target.Parameters.Count || this.parameterArguments[parameter] != -1 ||
                !ReferenceEquals(acquisition.Source, sourceArgument) ||
                (isDefault && (parameter <= previousDefault || !ReferenceEquals(target.Parameters[parameter].DefaultValue, omitted.Expression) ||
                    !ReferenceEquals(omitted.Parameter.Scope.Owner, target))) ||
                acquisition.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.BaseBorrow or ArgumentOperationKind.PayloadProjection or ArgumentOperationKind.ReferenceRead) || acquisition.ParameterIndex != parameter ||
                !ReferenceTypes.StorageMatches(intrinsic ? parameterType : generic?.Parameters[parameter] ?? creation?.Payload ?? target.Parameters[parameter].Type.BoundType, parameterType) ||
                (acquisition.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead) && !ReferenceTypes.IsString(parameterType) && !ReferenceTypes.IsBorrow(parameterType)))
            {
                return Fail("Invalid call argument mapping or acquisition.", out failure);
            }

            if (isDefault)
            {
                previousDefault = parameter;
            }

            // The builder omits CallEntry for an argument whose evaluation cannot complete.
            // It still checks the rest of the source and this call's signature.
            if ((isDefault && !complete) || !this.flow!.Nodes[sourceArgument].CanCompleteNormally)
            {
                this.parameterArguments[parameter] = -2;
                complete = false;
                continue;
            }

            if (cursor == this.arguments.Count)
            {
                return Fail("Missing acquired call argument.", out failure);
            }

            var entry = this.arguments[cursor++];
            var place = body.Operations[entry].Place;
            var type = body.Places[place].Type;
            // SPEC 15.4.4: an instance fits the argument to an Origin this body's local initializer inferred through the substituted bound.
            var fitted = this.instance is { } instanceCall && acquisition.ParameterType is { } declared ? this.instanceBinding!.InstantiateInferredType(declared, parameterType, instanceCall) : parameterType;
            if (!ReferenceEquals(body.Operations[entry].Source, call) ||
                (fitted is not { } required || (!call.CodeContext.Compilation.Binding.FitsVerifiedTypeAt(type, required, call) &&
                !(acquisition is { Kind: ArgumentOperationKind.BaseBorrow, BasePath: not null, ObjectCompatibility: ConstraintProof.Proven } &&
                type is { Semantics: SemanticsKind.Ref, Components.Count: 1 } && required is { Semantics: SemanticsKind.Ref, Components.Count: 1 } &&
                ReferenceEquals(required.Components[0], plan.DeclaringType) && ObjectTypes.Supports(type.Components[0], required.Components[0])))))
            {
                return Fail("Call entry does not match its argument Type or call.", out failure);
            }

            if (!this.ValidateCallArgumentStorage(body, entry, id, out failure))
            {
                return false;
            }

            if (ReferenceTypes.IsString(type) && this.referenceRoots[entry] >= 0)
            {
                // The instantiated parameter Type was matched against the callee's entry above; a
                // monomorphized instance forwards its ref/T parameter as the substituted string reference.
                if (!ReferenceTypes.IsString(parameterType) || !this.ValidateReferenceUse(body, entry, id) ||
                    !ReferenceEquals(acquisition.Source, sourceArgument) || !ReferenceEquals(SignatureType(this, acquisition.SourceType), SignatureType(this, sourceArgument.BoundType)))
                {
                    return Fail("Reference argument lacks its call-wide Loan or Origin substitution.", out failure);
                }

                var root = this.referenceRoots[entry];
                var sourceType = body.ConcreteAt(isDefault && acquisition.SourceType is { } declaredSource ? call.CodeContext.Compilation.Binding.InstantiateStorageType(declaredSource, plan) : acquisition.SourceType, id);
                if (acquisition.Kind == ArgumentOperationKind.Borrow
                    ? this.callLoanPlans[id] < 0 || body.Values[root].Kind != OwnershipValueKind.Borrow || !ReferenceEquals(body.ComparisonLoans[body.LoanStates[root]].Call, call) ||
                        !ReferenceEquals(body.Operations[root].Source, OwnershipAnalysis.BorrowedArgumentSource(sourceArgument))
                    : acquisition.Kind == ArgumentOperationKind.ReferenceRead
                    ? body.Values[root].Kind != OwnershipValueKind.PointerLoad || !ReferenceTypes.IsString(ValueType(body, root))
                    : (!isDefault && body.Values[root].Kind is not (OwnershipValueKind.Parameter or OwnershipValueKind.Address) && body.Operations[root].Kind != OwnershipOperationKind.Read) ||
                        !ReferenceEquals(ValueType(body, root), sourceType))
                {
                    return Fail("Reference acquisition does not match its source.", out failure);
                }
            }

            this.parameterArguments[parameter] = entry;
        }

        if (cursor != this.arguments.Count || (!complete && body.IsReachable(id)))
        {
            return Fail("Call has extra arguments or follows a noncompleting argument.", out failure);
        }

        this.arguments.Clear();
        if (!complete)
        {
            return true;
        }

        if (SlotTypes.IsResult(returnType) && !this.ValidateSlotCallResult(body, id, out failure))
        {
            return false;
        }

        if (comparison)
        {
            return this.LowerBuiltinComparison(body, function, plan, id, out failure);
        }

        if (arithmetic)
        {
            return this.LowerBuiltinArithmetic(body, function, constants, directory, plan, id, out failure);
        }

        this.callOperands.Clear();
        long writerKind = -1;
        EmissionOperand writerDispatch = default;
        if (plan.Target.CompilerFunction == CompilerFunctionKind.TextWriter &&
            !this.PrepareWriterDispatch(plan, function, returnType, out writerKind, out writerDispatch, out failure))
        {
            return false;
        }

        if (writerKind == 1 && call.Parent?.Formatting is { } adapterPlan && ReferenceEquals(call, adapterPlan.Adapter) && this.EstimateFormatting(adapterPlan).Stack)
        {
            writerKind = 0;
        }

        var location = -1;
        for (var i = 0; i < callee!.Parameters.Length; i++)
        {
            var physical = callee.Parameters[i];
            if (creation is { } objectCreationPlan && physical.Kind == AbiParameterKind.Context)
            {
                this.callOperands.Add(new(EmissionOperandKind.Integer, objectCreationPlan.Result.Semantics == SemanticsKind.Obj ? 0 : 2));
                continue;
            }

            if (physical.Kind == AbiParameterKind.Context && physical.Type == "i32" && plan.Target.CompilerFunction is CompilerFunctionKind.WriterWrite or CompilerFunctionKind.BuiltinFormat or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat)
            {
                var kind = this.BuiltinFormatKind(plan);
                if (kind < 0)
                {
                    return Fail("Formatting requires a selected encoder with a verified representation.", out failure);
                }

                this.callOperands.Add(new(EmissionOperandKind.Integer, kind));
                continue;
            }

            if (physical.Kind == AbiParameterKind.Context && plan.Target.CompilerFunction == CompilerFunctionKind.TextWriter)
            {
                this.callOperands.Add(physical.Type == "i64" ? new(EmissionOperandKind.Integer, writerKind) : writerDispatch);
                continue;
            }

            if (formatting && physical.Kind == AbiParameterKind.Context && plan.Target.CompilerFunction is CompilerFunctionKind.TextFixed or CompilerFunctionKind.TextTryFormat)
            {
                var destination = plan.Target.CompilerFunction == CompilerFunctionKind.TextFixed ? 0 : 1;
                if (SignatureType(this, plan.ArgumentOperations[destination].ParameterType) is not { Components.Count: 1 } borrowed ||
                    borrowed.Components[0] is not { Kind: BoundTypeKind.FixedArray, Length: >= 0 } array)
                {
                    return Fail("Fixed buffer requires a concrete initialized byte array.", out failure);
                }

                this.callOperands.Add(new(EmissionOperandKind.Integer, array.Length));
                continue;
            }

            if (physical.Kind == AbiParameterKind.ResultSlot)
            {
                if (!target.IsConstructor && !FunctionAbi.HasResultSlot(returnType, this.aggregateLayouts))
                {
                    return Fail("Physical result slot has no stored result representation.", out failure);
                }

                this.callOperands.Add(new(EmissionOperandKind.SlotAddress, operation.Place));
                continue;
            }

            if (physical.Kind is AbiParameterKind.Location or AbiParameterKind.LocationLength)
            {
                if (callee.CallerLocation && function.Abi.CallerLocation)
                {
                    this.callOperands.Add(new(physical.Kind == AbiParameterKind.Location ? EmissionOperandKind.CallerLocation : EmissionOperandKind.CallerLocationLength, 0));
                    continue;
                }

                if ((!runtime && creation is null && !callee.CallerLocation) || (location < 0 && !this.TryGetLocation(ReferenceEquals(plan.Target, library.Abort) ? call.Parent! : call, directory, constants, out location)))
                {
                    return Fail("A runtime call has no diagnostic source location.", out failure);
                }

                this.callOperands.Add(new(physical.Kind == AbiParameterKind.Location ? EmissionOperandKind.ConstantAddress : EmissionOperandKind.ConstantLength, location));
                continue;
            }

            if ((uint)physical.LogicalIndex >= (uint)target.Parameters.Count)
            {
                return Fail("Physical parameter has no logical argument.", out failure);
            }

            var entry = this.parameterArguments[physical.LogicalIndex];
            var type = formatting || clone ? body.Places[body.Operations[entry].Place].Type : generic?.Parameters[physical.LogicalIndex] ?? creation?.Payload ?? target.Parameters[physical.LogicalIndex].Type.BoundType!;
            if (!this.TryCallArgumentOperand(body, entry, id, type, physical.Kind, out var operand, out failure))
            {
                return false;
            }

            this.callOperands.Add(operand);
        }

        var expectedResult = FunctionAbi.ResultType(returnType, this.aggregateLayouts);
        if (expectedResult != callee.Result || callee.NoReturn != ReferenceEquals(returnType, BoundType.Never) ||
            callee.ResultSlot != (target.IsConstructor || FunctionAbi.HasResultSlot(returnType, this.aggregateLayouts)) ||
            this.callOperands.Count != callee.Parameters.Length ||
            (IsScalar(returnType) && (body.Values[id].Kind != OwnershipValueKind.Call || !ReferenceEquals(ValueType(body, id), returnType))))
        {
            return Fail("Call result or argument plan does not match its physical ABI.", out failure);
        }

        if (call.Parent?.Formatting is { } bufferPlan && ReferenceEquals(call, bufferPlan.Heap) && this.EstimateFormatting(bufferPlan) is { Stack: true } stack)
        {
            var region = function.FormattingStacks.Count;
            function.FormattingStacks.Add((int)stack.Capacity);
            function.AddCall(id, WindowsLowering.GetCompilerFunction(CompilerFunctionKind.TextFixed)!, [this.callOperands[0], new(EmissionOperandKind.FormattingStack, region), this.callOperands[1]]);
        }
        else
        {
            function.AddCall(id, callee, CollectionsMarshal.AsSpan(this.callOperands), virtualSlot, virtualReceiver);
        }

        this.formattingRuntimeUsed |= formatting;
        if (callee.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, id);
        }

        return true;
    }
}
