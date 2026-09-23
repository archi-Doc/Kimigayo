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

    internal IReadOnlyDictionary<BoundCall, GenericStoragePlan.CallEntry>? GenericCalls { get; set; }

    internal IReadOnlyDictionary<BoundCall, ObjectCall>? ObjectCalls { get; set; }

    internal void ClearFunctionContext()
    {
        this.functions = null;
        this.GenericCalls = null;
        this.ObjectCalls = null;
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
        if (operation.Source is InvocationKoto { BoundValueCall: { } valueCall } invocation)
        {
            return this.LowerValueCall(body, function, id, invocation, valueCall, out failure);
        }

        var generic = operation.Source is InvocationKoto { BoundCall: { } bound } ? this.GenericCalls?.GetValueOrDefault(bound) ?? this.ForwardedEntry(bound) : null;
        var creation = operation.Source is InvocationKoto { BoundCall: { } objectCall } ? this.ObjectCalls?.GetValueOrDefault(objectCall) : null;
        var original = (operation.Source as InvocationKoto)?.BoundCall;
        var directIndex = original is not null && this.instanceEntry?.ConcreteCalls is not null ? Array.IndexOf(this.instanceEntry.Template.DirectCalls, original) : -1;
        var resolved = directIndex >= 0 ? this.instanceEntry!.ConcreteCalls![directIndex] : original;
        if (operation.Source is not InvocationKoto { AttributeChain: null } call || resolved is not { } plan ||
            (generic is null && creation is null && plan.TypeArguments.Length != 0) ||
            plan.Target.Declaration is not FunctionKoto target || plan.ArgumentOperations.Length != call.ArgumentNodes.Count ||
            plan.ArgumentToParameter.Length != call.ArgumentNodes.Count || call.ArgumentNodes.Count + plan.DefaultArguments.Length + (plan.Receiver is null ? 0 : 1) != target.Parameters.Count ||
            !ReferenceEquals(SignatureType(this, call.BoundType), SignatureType(this, plan.ReturnType)) || SignatureType(this, plan.ReturnType) is not { } returnType ||
            !ReferenceTypes.StorageMatches(generic?.Result ?? creation?.Result ?? (target.IsConstructor ? plan.DeclaringType : target.BoundSymbol?.Type), returnType))
        {
            return Fail("A call needs unsupported callee, argument acquisition or result lowering.", out failure);
        }

        var runtime = ReferenceEquals(plan.Target, library.WriteLine) || ReferenceEquals(plan.Target, library.Abort) || ReferenceEquals(plan.Target, library.GetSymbol(KimiDeclarationId.TestTempDirectory));
        // A selected explicit specialization (SPEC 21.3.4) is called directly; its ABI is the entry's ABI.
        var callee = runtime ? WindowsLowering.GetCompilerFunction(plan.Target.CompilerFunction) : creation?.Physical.Abi ?? generic?.Selected ?? generic?.Abi ?? this.functions!.GetValueOrDefault(target);
        if (callee is null)
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
            var parameterType = SignatureType(this, acquisition.ParameterType);
            if ((uint)parameter >= (uint)target.Parameters.Count || this.parameterArguments[parameter] != -1 ||
                !ReferenceEquals(acquisition.Source, sourceArgument) ||
                (isDefault && (parameter <= previousDefault || !ReferenceEquals(target.Parameters[parameter].DefaultValue, omitted.Expression) ||
                    !ReferenceEquals(omitted.Parameter.Scope.Owner, target) || !ScalarDefaults.SupportsValue(omitted.ParameterType))) ||
                acquisition.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection) || acquisition.ParameterIndex != parameter ||
                !ReferenceTypes.StorageMatches(generic?.Parameters[parameter] ?? creation?.Payload ?? target.Parameters[parameter].Type.BoundType, parameterType) ||
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
            if (!ReferenceEquals(body.Operations[entry].Source, call) ||
                (parameterType is not { } required || !call.CodeContext.Compilation.Binding.FitsTypeAt(type, required, call)))
            {
                return Fail("Call entry does not match its argument Type or call.", out failure);
            }

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

            if (ReferenceTypes.IsString(type))
            {
                // The instantiated parameter Type was matched against the callee's entry above; a
                // monomorphized instance forwards its ref/T parameter as the substituted string reference.
                if (!ReferenceTypes.IsString(parameterType) || !this.ValidateReferenceUse(body, entry, id) ||
                    !ReferenceEquals(acquisition.Source, call.ArgumentNodes[i]) || !ReferenceEquals(acquisition.SourceType, call.ArgumentNodes[i].BoundType))
                {
                    return Fail("Reference argument lacks its call-wide Loan or Origin substitution.", out failure);
                }

                var root = this.referenceRoots[entry];
                if (acquisition.Kind == ArgumentOperationKind.Borrow
                    ? this.callLoanPlans[id] < 0 || body.Values[root].Kind != OwnershipValueKind.Borrow || !ReferenceEquals(body.ComparisonLoans[body.LoanStates[root]].Call, call) ||
                        !ReferenceEquals(body.Operations[root].Source, OwnershipAnalysis.BorrowedArgumentSource(call.ArgumentNodes[i]))
                    : (body.Values[root].Kind is not (OwnershipValueKind.Parameter or OwnershipValueKind.Address) && body.Operations[root].Kind != OwnershipOperationKind.Read) ||
                        !ReferenceEquals(ValueType(body, root), SignatureType(this, acquisition.SourceType)))
                {
                    return Fail("Reference acquisition does not match its source.", out failure);
                }
            }

            if (body.IsReachable(id) && !this.Dominates(entry, id))
            {
                return Fail("Logical call argument does not dominate the call.", out failure);
            }

            if (SlotTypes.IsResult(type) && place == operation.Place)
            {
                return Fail("A call cannot share its argument and result storage.", out failure);
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

        this.callOperands.Clear();
        var location = -1;
        for (var i = 0; i < callee.Parameters.Length; i++)
        {
            var physical = callee.Parameters[i];
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
                if ((!runtime && creation is null) || (location < 0 && !this.TryGetLocation(ReferenceEquals(plan.Target, library.Abort) ? call.Parent! : call, directory, constants, out location)))
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
            var type = generic?.Parameters[physical.LogicalIndex] ?? creation?.Payload ?? target.Parameters[physical.LogicalIndex].Type.BoundType!;
            if (physical.Kind == AbiParameterKind.Value && IsScalar(type))
            {
                var value = Input(body, entry, 0);
                if (body.IsReachable(id) && !this.Dominates(value, id))
                {
                    return Fail("Call argument does not dominate the call.", out failure);
                }

                this.callOperands.Add(this.PhysicalOperand(body, value));
            }
            else if (physical.Kind == AbiParameterKind.SharedReference && ReferenceTypes.IsString(type))
            {
                this.callOperands.Add(this.ReferenceOperand(body, entry));
            }
            else if (physical.Kind == AbiParameterKind.OwnedSlot && SlotTypes.IsResult(type))
            {
                this.callOperands.Add(new(EmissionOperandKind.SlotAddress, body.Operations[entry].Place));
            }
            else
            {
                return Fail("Unsupported physical call argument.", out failure);
            }
        }

        var expectedResult = FunctionAbi.ResultType(returnType, this.aggregateLayouts);
        if (expectedResult != callee.Result || callee.NoReturn != ReferenceEquals(returnType, BoundType.Never) ||
            callee.ResultSlot != (target.IsConstructor || FunctionAbi.HasResultSlot(returnType, this.aggregateLayouts)) ||
            this.callOperands.Count != callee.Parameters.Length ||
            (IsScalar(returnType) && (body.Values[id].Kind != OwnershipValueKind.Call || !ReferenceEquals(ValueType(body, id), returnType))))
        {
            return Fail("Call result or argument plan does not match its physical ABI.", out failure);
        }

        function.AddCall(id, callee, CollectionsMarshal.AsSpan(this.callOperands));
        if (callee.NoReturn)
        {
            function.Add(EmissionOpcode.Unreachable, id);
        }

        return true;
    }
}
